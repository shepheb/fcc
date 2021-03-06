\ Uses the assembler from dcpu/bootstrap/asm.fs to build a DCPU-16 Forth.
\ Overall design: Indirect threading, with literals inline.
\ Registers:
\ - J holds the return-stack pointer.
\ - I holds the next-codeword pointer.
\ - SP holds the data stack pointer.
\ Both stacks are full-descending. Return stack lives at the top of memory,
\ data stack below it. 1K reserved for the return stack.

\ Main routine right at the top:
\ Initialize the stack pointers,

HEX
fc00 CONSTANT data-stack-top
0    CONSTANT return-stack-top
DECIMAL

\ Compile a jump to the main routine, will be fixed at the bottom. later on.
0 long-lit rpc SET,
DH 1- CONSTANT main-addr

\ Some of the doers rely on the codeword landing in register A.
: NEXT,
  [ri] ra set, \ A now points at the codeword. That should be loaded into PC.
  [ra] rpc set,
;

\ Writes from some src operand to the return stack.
\ Example use: ra pushrsp,    or    rpop pushrsp,
: PUSHRSP, ( src -- )
  1 lit rj sub,
  ( src ) [rj] set,
;

\ Pops from the return stack into the specified operand.
\ Example use: ra poprsp,    or    rpush pushrsp,
: POPRSP, ( src -- )
  [rj] swap set,
  1 lit rj add,
;

\ Holds the DCPU address of the most-recently-compiled word.
VARIABLE last-word
0 last-word !

\ Assembles the header for a word with the given name.
\ Word headers look like this:
\ - Link pointer.
\ - Name length/metadata
\ - Name.... (Unpacked, one character per word. No terminator.)
\ - Codeword (PC should be set to this value.)
\ - code....
: :WORD
  DH   last-word @ h,   last-word ! ( )
  parse-name ( c-addr u )
  dup h,
  DSTR, ( ) \ Compiles the string in as a literal.
  DH 1+ h, \ Write the address of the next word into this word.
  \ Now we're ready for the assembly code to be added.
  \ Note that we don't actually enter the real compiling mode here!
;

: ;WORD next, ;

\ Does nothing, no next.
: ;WORD-BARE ;

512 CONSTANT F_IMMEDIATE
256 CONSTANT F_HIDDEN
255 CONSTANT MASK_LEN
F_HIDDEN MASK_LEN OR CONSTANT MASK_LEN_HIDDEN

\ Makes the previously-defined DCPU word immediate.
: DIMMEDIATE last-word @ 1+ dup h@ F_IMMEDIATE or swap h! ;

: BINOP ( xt "<spaces>name" -- )
  >R :WORD
    rpop ra set,
    ra rpeek R> execute
  ;WORD
;

\ Math ops
' add, BINOP +
' sub, BINOP -
' mul, BINOP *
' dvi, BINOP /
' mdi, BINOP MOD

\ Bitwise operations
' and, BINOP AND
' bor, BINOP OR
' xor, BINOP XOR

\ Shifts. Forth's default RSHIFT is unsigned/logical, so use SHR.
' shl, BINOP LSHIFT
' shr, BINOP RSHIFT
' asr, BINOP ARSHIFT

\ Comparison, using the branch instructions.
: CMPOP ( xt "<spaces>name" -- )
  >R :WORD
    0 lit ra set,
    rpop rb set,
    rpop rb R> execute
      -1 lit ra set,
    ra rpush set,
  ;WORD
;

' ifa, CMPOP <
' ifg, CMPOP U<
' ife, CMPOP =

\ Stack ops
:WORD DUP  rpeek rpush set, ;WORD
:WORD DROP rpop ra set, ;WORD
:WORD SWAP
  rpop ra set,
  rpop rb set,
  ra rpush set,
  rb rpush set,
;WORD
:WORD >R rpop pushrsp, ;WORD
:WORD R> rpush poprsp, ;WORD

:WORD DEPTH
  data-stack-top lit ra set,
  rsp ra sub, \ ra = top - sp
  ra rpush set,
;WORD


\ Takes two values: the host CONSTANT name, and the target word.
: DVAR ( "<spaces>host_name <spaces>word_name" -- )
  DH dup CONSTANT
  0 h,
  :WORD lit rpush set, ;WORD
;

DVAR VAR-BASE BASE
DVAR VAR-STATE STATE
DVAR VAR-DSP (>HERE)

\ Unimplemented: (ALLOCATE) - probably based on (>HERE)?

:WORD @ rpop ra set,  [ra] rpush set, ;WORD
:WORD ! rpop ra set,  rpop [ra] set, ;WORD
\ C@ and C! are identical to @ and ! when chars and cells are the same size.
:WORD C@ rpop ra set,  [ra] rpush set, ;WORD
:WORD C! rpop ra set,  rpop [ra] set, ;WORD

\ Pushes I, the next-word pointer, to the return stack, loads I with the data,
\ and does NEXT.
:WORD EXECUTE
  ri pushrsp,
  rpop ri set,
;WORD

\ Unconditional jump by the delta in I.
\ I love this code!
:WORD (BRANCH) [ri] ri add, ;WORD
:WORD (0BRANCH)
  1 lit ra set,
  0 lit rpop ife,
    [ri] ra set,
  ra ri add,
;WORD

:WORD EXIT
  DH 1- CONSTANT cfa-EXIT \ cfa-EXIT can be used by ; below
  ri poprsp,
;WORD

DVAR VAR-LATEST (LATEST)

\ Turns a header address into a code field address.
:WORD (>CFA)
  rpop ra set,
  1 lit ra add,
  [ra] ra add,
  1 lit ra add,
  ra rpush set,
;WORD

\ Only valid for CREATEd words, so this is cfa+2
:WORD >BODY ( cfa -- data-addr )
  2 lit rpeek add,
;WORD


\ "Doer" words. Each of these has a [bracketed] CONSTANT giving their DCPU
\ address; this enables easy compilation elsewhere.
:WORD (DOCOL)
  DH CONSTANT [DOCOL]
  \ NEXT, leaves the code field address in A. Bump it to the next cell and NEXT.
  ri pushrsp,
  1 lit ra add,
  ra ri set,
;WORD

:WORD (DOLIT)
  DH CONSTANT [DOLIT]
  [ri] rpush set,
  1 lit ri add,
;WORD

:WORD (DOSTRING)
  DH CONSTANT [DOSTRING]
  [ri] ra set,
  1 lit ri add,
  ri rpush set, \ c-addr
  ra rpush set, \ c-addr u
  ra ri add,
;WORD

:WORD (DODOES)
  DH CONSTANT [DODOES]
  \ Pushes cfa+2. Check cfa+1, if nonzero, jump there a la EXECUTE.
  ra rb set,
  2 lit rb add,
  rb rpush set,
  1 [ra+] ra set,
  0 lit ra ifn,
  IF,
    ri pushrsp,
    ra ri set,
  THEN,
;WORD



\ Parsing - never mind for now how parse-buf gets filled!
\ Input sources: 5 words each, as follows:
\ type (0 = keyboard, -1 = EVALUATE, 1 block)
\ block number (undefined if not a block source)
\ address of parse buffer
\ size of parse buffer
\ index into parse buffer (this is effectively >IN)

\ Since the keyboard can't be nested, it has a singular buffer.
\ NB: If you call BLOCK or BUFFER while the input source is a block, be careful.
\ LOAD and THRU carefully do this safely.

\ Blocks are read in pseudo-lines, copied and expanded into block-parse-buffer.
\ That region can be shared as blocks are nested; the REFILL logic will copy
\ anew where needed (whenever the source changes to a block, it loads that
\ block, and copies the line starting at the offset of source-buffer.

0
dup CONSTANT source-type 1+
dup CONSTANT source-block 1+
dup CONSTANT source-buffer 1+
dup CONSTANT source-size 1+
dup CONSTANT source-index 1+
CONSTANT /source

DH CONSTANT var-source-index
0 h,

DH CONSTANT INPUT-SOURCES
/source 16 * allot,

\ Used for keyboard parsing.
DH CONSTANT PARSE-BUF
256 allot,

\ The plan for basic parsing:
\ - Expect the delimiter in A
\ - Load the base pointer for the input source into B.
\ - Load the pointer for >IN into C.
\ - X is the start of the parsed word.
\ - Y is the roving next-character pointer.
\ - Z is the pointer of the word past the end.
\ - Advance Y until it equals Z or until [Y] == A
\ - When Y == Z, failed parse, return 0 0
\ - When Y < Z, successful parse. Return X Y-X
\ - Either way, reset >IN to Y. (Advance Y by 1 more if successfully parsed.)

\ Returns the start address in X and the count in C.
DH CONSTANT code-parse
  rpop ra set, \ A - delimiter.

  var-source-index [lit] rb set,
  /source lit rb mul,
  input-sources lit rb add, \ B - input_source*

  rb rc set,
  source-index lit rc add, \ C - *>IN - pointer to index into parse buffer

  \ Compute, and push, the start of the parsing region.
  source-buffer [rb+] rx set, \ X - Start of the whole buffer
  [rc] ry set,
  rx ry add,                  \ Y - Current address inside the parse buffer.
  ry rpush set,               \ Which is pushed, it's part of the output.

  source-size [rb+] rz set,  \ length of parsed area
  rx rz add,                 \ Z - Address past the end of the parse buffer.

  begin,
    rz ry ifl,   \ Continue while Y < Z
    [ry] ra ifn, \ and [Y] != A
  while, \ Writes a branch to past the end, we want to skip it.
    1 lit ry add,
  repeat,

  \ Push the length, first. It might be 0, but that's fine.
  rx ry sub, \ Y - the length parsed
  ry rpush set, \ Pushed!

  \ Now check if Y < Z; if that's still true then we found a delimiter.
  rz ry ifl, \ Backwards: skip when Y = Z
  if,
    \ Advance the pointer one more, past the delimiter.
    1 lit ry add,
  then,

  ry [rc] add,  \ And add the length parsed to the >IN

  \ Pop the two values from the stack into X and C, and return.
  rpop rc set,
  rpop rx set,
  rpop rpc set,

:WORD PARSE
  rpop ra set,
  code-parse lit jsr,
  rx rpush set,
  rc rpush set,
;WORD

\ Skips leading delimiters, then parses to a space.
\ Returns the length in C, address in X.
\ Clobbers all kinds of things.
DH CONSTANT code-parse-name
  32 lit ra set, \ A - the delimiter

  var-source-index [lit] rb set,
  /source lit rb mul,
  input-sources lit rb add, \ B - input_source*

  rb rc set,
  source-index lit rc add, \ C - *>IN - pointer to index into parse buffer

  \ Compute, and push, the start of the parsing region.
  source-buffer [rb+] rx set, \ X - Start of the whole buffer
  [rc] ry set,
  rx ry add,                  \ Y - Current address inside the parse buffer.

  source-size [rb+] rz set,  \ length of parsed area
  rx rz add,                 \ Z - Address past the end of the parse buffer.

  begin,
    rz ry ifl,   \ Continue while Y < Z
    [ry] ra ife, \ and [Y] == A
  while,
    1 lit ry add,
  repeat,

  \ Now Y is pointed at a non-delimiter.
  \ Update >IN based on the leading spaces.
  rx ry sub, \ Y - the length parsed
  ry [rc] add,

  \ A holds the delimiter, so call into code-parse.
  \ It returns the same things I want to, so no JSR, just SET PC
  code-parse lit rpc set, \ Now C = len, X = addr


:WORD PARSE-NAME
  code-parse-name lit jsr,
  rx rpush set,
  rc rpush set,
;WORD


\ Attempts to convert a double-cell value to a number.
\ Does double-cell math properly here.
:WORD >NUMBER ( ud1 c-addr1 u1 -- ud2 -- c-addr2 ud2 )
  rpop rc set, \ C - count of characters remaining.
  rpop rx set, \ X - address of current character.
  rpop rb set, \ B - hi word
  rpop ra set, \ A - lo word
  var-base lit rz set, \ Z - base

  begin,
    [rc] ry set, \ Read the new digit character into Y
    [char] 0 lit ry sub, \ Adjust so '0' -> 0
    9 lit ry ifg, \ When the new digit is > 9
    if,
      [char] A [char] 0 - lit ry sub, \ Now 'A' = 0
      25 lit ry ifg, \ When the new digit is still > 25, try lowercase.
      if,
        [char] a [char] A - lit ry sub, \ Now 'a' = 0
        10 lit ry add, \ Add back 10. Y is the correct numerical value.
      else,
        10 lit ry add, \ Add back 10. Y is the correct numerical value.
      then,
    then,

    \ Either way, Y is the correct would-be numerical value of this digit.
    \ Need to check that it's less than base (Z).
    rz ry ifl, \ Y < Z
  while,
    rz rb mul,   \ Multiply the high word by the base, first.
    rz ra mul,   \ Then lo*base
    rex rb add,  \ Add the overflow into hi

    ry ra add,   \ Add the new digit into lo,
    rex rb add,  \ and its overflow into hi.

    1 lit rc sub,
    1 lit rx add,
  repeat,

  \ Now put everything back on the stack.
  ra rpush set, \ lo
  rb rpush set, \ hi
  rx rpush set, \ address
  rc rpush set, \ count unconverted
;WORD


\ Parses a word and assembles a new (partial) dictionary header for it.
\ Returns the code field address in A.
DH CONSTANT code-(CREATE)
  \ Read the current (target) data space pointer, because that's the new LATEST.
  var-dsp lit ra set, \ A - *dsp
  [ra] rb set,        \ B - dsp
  \ Write a pointer to the old latest at DSP
  var-latest lit rc set, \ C *latest
  [rc] [rb] set,

  \ Update LATEST to be the new value.
  rb [rc] set,
  1 lit rb add, \ Bump B (dsp) by one.

  \ Parse a name,
  code-parse-name lit jsr, \ X = addr, C = len

  \ Write its length at [B]
  rc [rb] set,
  begin,
    0 rc ifg,
  while,
    1 lit rc sub,
    1 lit rb add,
    [rx] [rb] set, \ Write a character to the header.
    1 lit rx add,
  repeat,

  \ Now C=0 and B = codeword slot
  rb rx set, \ Set aside the codeword address.

  \ Update the DSP to after the codeword.
  1 lit rb add,
  rb [ra] set,
  rx ra set, \ Copy the code field address to A for return.
  rpop rpc set,


:WORD CREATE
  \ First, call into code-(CREATE) to get a partial header.
  code-(CREATE) lit jsr,
  \ Now A = code field address.
  \ Write [DODOES] into it.
  [DODOES] lit [ra] set,
  0 lit 1 [ra+] set, \ Write a 0 after the codeword; the DOES> slot.

  \ Now we need to bump DSP to after that DOES> address.
  var-DSP lit rb set,
  1 lit [rb] add,
  \ New word created and ready!
;WORD


\ Expects one string in X/C, another in Y/Z.
\ Returns 0 in A if they're different, 1 if they're the same.
DH CONSTANT code-strcmp
  \ If C != Z, return 0 right now.
  MASK_LEN_HIDDEN lit rc and,
  MASK_LEN_HIDDEN lit rz and,
  rc rz ifn,
  if,
    0 lit ra set,
    rpop rpc set, \ return 0 now
  then,

  \ Now loop through the strings together.
  begin,
    0 lit rc ifg,
  while,
    [rx] [ry] ifn,
    if, \ Don't match, return 0
      0 lit ra set,
      rpop rpc set,
    then,
    1 lit rx add,
    1 lit ry add,
    1 lit rc sub,
  repeat,

  1 lit ra set,
  rpop rpc set,


DH CONSTANT code-(find)
  \ Expects X to be a character address, C the number of characters.
  \ Saves I on the stack and stores the link pointer there.
  ri rpush set,
  var-latest [lit] ri set,

  begin,
    0 lit ri ifn,
  while,
    \ Load the address of the name into Y, the length into Z.
    ri ry set,
    2 lit ry add,   \ Y = address
    1 [ri+] rz set, \ Z = length

    rx rpush set,
    rc rpush set,
    code-strcmp lit jsr, \ A is now the equality flag
    rpop rc set,
    rpop rx set,

    \ Now abusing repeat, it's supposed to be unconditional, but I can arrange
    \ to skip over it.
    0 lit ra ifn, \ When A = 0, we haven't found anything: don't skip.
  repeat,

  \ Down here, X and C are still the name, I is the header.
  \ Returns I, which might be 0, in A.
  ri ra set,
  rpop ri set,
  rpop rpc set,


:WORD (FIND)
  rpop rc set,
  rpop rx set,
  code-(find) lit jsr,

  \ Now A is the address of the header, maybe 0.
  0 lit ra ife,
  if,
    0 lit rpush set,
    0 lit rpush set,
  else,
    ra rpush set,
    -1 lit rb set,
    1 [ra+] rc set, \ The length and metadata word.
    F_IMMED lit rc ifb, \ bits in common
      1 lit rb set,    \ so it's immediate, set to 1
    rb rpush set,
  then,
;WORD


\ Defining words
:WORD :
  \ Call (CREATE), which builds a partial header.
  code-(CREATE) lit jsr,
  \ Now A is the code field address.
  [DOCOL] lit [ra] set, \ Which we fill with DOCOL
  \ Everything is ready for the compilation to write xt's into this definition.
  \ So I just need to set compilation mode and stand back.
  \ TODO New : words need to be hidden, new CREATE words not. Now, neither is.
  var-STATE lit rx set,
  1 lit [rx] set,
;WORD

:WORD ;
  \ Compile EXIT into the definition.
  var-DSP lit rx set, \ X - *dsp
  [rx] ry set,        \ Y - dsp
  cfa-EXIT lit [ry] set, \ Compile the EXIT
  1 lit ry add,
  ry [rx] set,        \ Write the new DSP

  \ Finally, switch back to interpreting mode.
  var-STATE lit rx set,
  0 lit [rx] set,
;WORD


\ TODO Use the display instead of the serial terminal.
\ Expects B to be the character to push. Clobbers A, C.
DH CONSTANT code-EMIT
  2 lit ra set,
  var-HW-SERIAL lit rc set,
  [rc] hwi,
  rpop rpc set,

:WORD EMIT
  rpop rb set,
  code-EMIT lit jsr,
;WORD


\ Returns the next key typed (serial!) in C. Clobbers A, B.
DH CONSTANT code-KEY
  var-HW-SERIAL lit rb set,
  1 lit ra set,
  begin,
    [rb] hwi,
    -1 lit rc ifn,
  until,
  \ Got a character!
  rpop rpc set,


:WORD KEY
  code-KEY lit jsr,
  rc rpush set,
;WORD


\ Reloads a line from the input source.
\ For the console, that means calling KEY repeatedly until the buffer
\ is filled, allowing for backspace.
\ For a block, that means loading the block into the buffer and then
\ copying the next pseudo-line into the block parsing buffer.
\ When a source is exhausted, pop the source. When landing on a block source,
\ the line is again copied into place.
\ TODO Block support!
\ Returns the REFILL success/failure flag in A.
\ Clobbers: A B C X Y
DH CONSTANT code-REFILL
  var-source-index [lit] rx set,
  /source lit rx mul,
  input-sources lit rx add, \ X - *source

  parse-buf lit   source-buffer [rx+] set, \ Update the buffer to parse-buf.
  0 lit           source-index  [rx+] set, \ And the index to 0.

  parse-buf lit ry set, \ Y is the start of the parse area.

  \ Now we keep the next-character address in Y, and start accepting characters.
  \ There are two special characters: backspace (8) and enter (10).
  \ Everything else gets written in.
  begin,
    code-KEY lit jsr, \ A B clobbered, C holds the character typed.
    8 lit rc ife,
    if, \ backspace
      \ move the pointer back by 1
      1 lit ry sub,
      \ but not before the start.
      parse-buf lit  ry ifl, \ Check Y < parse-buf
        parse-buf lit ry set, \ Reset it if less.
    else,
      10 lit rc ifn,
      if, \ not newline
        \ write the character into the buffer and bump the buffer.
        rc [ry] set,
        1 lit ry add,
      then,
    then,

    10 lit rc ife,
  until,

  \ We have a complete line. Subtract the parse-buf start from Y to get the
  \ length.
  parse-buf lit ry sub, \ Y = parse length
  ry   source-size [rx+] set, \ Written into the source.

  \ Refilling complete!
  \ Always return true for the keyboard, even if we got nothing.
  -1 lit ra set,
  rpop rpc set,


:WORD REFILL
  code-REFILL lit jsr,
  ra rpush set,
;WORD



\ QUIT process:
\ - Empty the stacks.
\ - Switch to interpreting.
\ - Keep reading input and trying to parse names from it.
\ - Handle each word found according to STATE.
DH CONSTANT code-QUIT
  \ Empty both stacks.
  data-stack-top   lit rsp set,
  return-stack-top lit rj  set,
  \ Switch to interpreting mode.
  0 lit  var-STATE [lit] set,

  \ Refill once before we start, dumping whatever was in the input when we
  \ first called QUIT.
  code-REFILL lit jsr,

  begin,
    begin,
      \ Try to parse a name from the input
      code-parse-name lit jsr, \ X = address, C = length.
      \ Loop until C != 0, meaning we found a word.
      0 lit rc ife,
    while,
      code-REFILL lit jsr,
    repeat,

    \ Now we've got a word loaded. X = address, C = length.
    \ And then try to (FIND) (preserves X, C, returns *header in A)
    code-(find) lit jsr,

    \ If it's 0, not found. Try to parse it as a number.
    0 lit ra ife,
    if, \ Not found, try to parse as a number.
    else, \ Found! Either execute or interpret.
      \ Check if the word is immediate.
      1 [ra+] rb set, \ B is the metadata word
      F_IMMED lit rc and, \ C now the IMMED flag

      \ Need to combine the immediacy flag and the interpreting mode somehow.


      var-STATE [lit] 0 lit ife,
      if,
        \ Interpreting: run the word.
        \ Requires setting I to something sane: a pointer just ahead.

        .... ri set,
        
    then,


  again, \ Infinite interpreter loop.

:WORD QUIT


\ - Debugging: `SEE` (optional)

\ TODO QUIT main()


\ Final output!
: WRITE-OUTPUT mem out @ ( c-addr u ) S" dcpu.bin" (dump-file) ;
WRITE-OUTPUT
