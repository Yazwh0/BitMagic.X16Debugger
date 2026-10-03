.export second

.segment "CODE"
second:
    ldx #$42
    rts

.segment "BANKCODE"
banked:
    ldy #$10
    rts

MAX_COUNT = 10

.segment "ZEROPAGE"
zp_pointer: .res 2

.segment "BSS"
counter:    .res 1
buffer:     .res 16

.segment "RODATA"
table:      .byte 1, 2, 3, 4
words:
    .word $1234
    .word $5678
    .word $9abc
after_words: .byte 0

.segment "CODE"
.proc work
.segment "BSS"
local_count: .res 1
.segment "CODE"
    lda table
    sta counter
    sta local_count
loop:
    dex
    bne loop
    rts
.endproc
