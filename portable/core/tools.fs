\ TOOLS word set.
\ .S is provided in the VM, as is SEE.
\ Also BYE and STATE.

: ? ( a-addr -- ) @ U. ;

\ Dumps a hexdump-style listing of memory contents.
HEX
: DUMP ( addr u -- )
  base @ >R
  HEX
  0 DO
    i 10 mod 0= IF cr dup i + 1 u.r [char] : emit THEN
    i  2 mod 0= IF space THEN
    dup i + c@ dup 10 < IF [char] 0 emit THEN 1 .R
  LOOP
  cr
  R> base !
;
DECIMAL

\ This is married to the structure of a dictionary header, and the dictionary
\ table in the portable VM. If either of those change, this needs to change too.
\ TODO Should this be hiding the hidden words? Internal words?
: WORDS ( -- )
  (LATEST) @
  BEGIN ?dup WHILE
    dup cell+ @ ( hdr metadata )
    255 and ( hdr len )
    over 2 cells + @ ( hdr len name-addr )
    swap type cr ( hdr )
    @ ( next-hdr )
  REPEAT
;

\ TOOLS EXT word set

: AHEAD ( -- ) ( C: -- orig ) ['] (branch) compile, HERE 0 , ; IMMEDIATE

: SYNONYM ( "<spaces>newname" "<spaces> oldname" -- )
  CREATE IMMEDIATE
  (latest) @ cell+ dup @ 256 or swap ! \ hide the new word
  ' \ grabs the next string in the input
  , \ compile oldname into the body
  (latest) @ cell+ dup @ 256 invert and swap ! \ reveal the new word again
  DOES>
    @ state @ 0= over cell+ @ 512 and or
    IF execute ELSE compile, THEN
;

\ N>R and NR> just put the elements on the return stack in reverse order,
\ which is the most convenient.
\ Remember that the return address for N>R and NR> is on top and needs to be
\ preserved.
VARIABLE (saved-return-address)
: N>R ( i*n +n -- ) ( R: -- j*n +n )
  R> (saved-return-address) !
  dup ( +n +n )
  BEGIN ?dup WHILE rot >R 1- REPEAT ( +n )
  >R
  (saved-return-address) @ >R
;

: NR> ( -- i*n +n ) ( R: j*n +n -- )
  R> (saved-return-address) !
  R> dup ( +n i )
  BEGIN ?dup WHILE 1- R> -rot ( val +n i ) REPEAT
  ( ... +n )
  (saved-return-address) @ >R
;


: [DEFINED] ( "<spaces>name" -- defined? ) bl word find nip 0<> ; IMMEDIATE
: [UNDEFINED] ( "<spaces>name" -- undefined? ) bl word find nip 0= ; IMMEDIATE

