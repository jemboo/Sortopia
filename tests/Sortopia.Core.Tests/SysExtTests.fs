namespace Sortopia.Core.Tests

open Xunit
open Sortopia.Core.SysExt


type SysExtTests() =

    [<Fact>]
    let ``UInt8 bit manipulation methods`` () =
        let value = 0b01011010uy
        let rot = 0b10100101uy
        Assert.Equal(value, value.rev)
        Assert.Equal(4, value.count)
        Assert.Equal(4, value.count_dense)
        Assert.Equal(0b01011000uy, value.diff 0b00000010uy)
        Assert.True((value.subset 0b01011011uy))
        Assert.False((value.propersubset value))
        Assert.Equal(rot, value.rotateLeft 4)
        Assert.Equal(rot, value.rotateRight 4)
        Assert.Equal(rot, value.rotate 4)
        Assert.Equal(0b01000000uy, value.leftmost_one)
        Assert.Equal(0b10000000uy, value.leftmost_zero)
        Assert.Equal(0b00000010uy, value.rightmost_one)
        Assert.Equal(0b00000001uy, value.rightmost_zero)
        Assert.Equal(1, value.rightmost_index)
        Assert.Equal(6, value.leftmost_index)

    [<Fact>]
    let ``UInt16 bit manipulation methods`` () =
        let value = 0b0101101010101010us
        Assert.Equal(0b0101010101011010us, value.rev)
        Assert.Equal(8, value.count)
        Assert.Equal(8, value.count_dense)
        Assert.Equal(0b0101101000000000us, value.diff 0b0000000010101010us)
        Assert.True((value.subset 0b1111101010101010us))
        Assert.False((value.propersubset 0b0101101010101010us))
        Assert.Equal(0b1010101010100101us, value.rotateLeft 4)
        Assert.Equal(0b1010010110101010us, value.rotateRight 4)
        Assert.Equal(0b1010101010100101us, value.rotate 4)
        Assert.Equal(0b1010010110101010us, value.rotate -4)
        Assert.Equal(0b0100000000000000us, value.leftmost_one)
        Assert.Equal(0b1000000000000000us, value.leftmost_zero)
        Assert.Equal(0b0000000000000010us, value.rightmost_one)
        Assert.Equal(0b0000000000000001us, value.rightmost_zero)
        Assert.Equal(1, value.rightmost_index)
        Assert.Equal(14, value.leftmost_index)

    [<Fact>]
    let ``UInt32 bit manipulation methods`` () =
        let value = 0b01011010010110100101101010101010u
        Assert.Equal(0b01010101010110100101101001011010u, value.rev)
        Assert.Equal(16, value.count)
        Assert.Equal(16, value.count_dense)
        Assert.Equal(0b01010000010110100101000000001010u, value.diff 0b00001010000001011010101010100000u)
        Assert.False((value.subset 0b11111010101011111000000011110000u))
        Assert.True((value.subset 0b11111010110111111101101011111010u))
        Assert.False((value.propersubset value))
        Assert.Equal(0b10100101101001011010101010100101u, value.rotateLeft 4)
        Assert.Equal(0b10100101101001011010010110101010u, value.rotateRight 4)
        Assert.Equal(0b10100101101001011010101010100101u, value.rotate 4)
        Assert.Equal(0b10100101101001011010010110101010u, value.rotate -4)
        Assert.Equal(0b01000000000000000000000000000000u, value.leftmost_one)
        Assert.Equal(0b10000000000000000000000000000000u, value.leftmost_zero)
        Assert.Equal(0b00000000000000000000000000000010u, value.rightmost_one)
        Assert.Equal(0b00000000000000000000000000000001u, value.rightmost_zero)
        Assert.Equal(1, value.rightmost_index)
        Assert.Equal(30, value.leftmost_index)

    [<Fact>]
    let ``UInt64 bit manipulation methods`` () =
        let value = 0b0101010110101010001100111100110000001111111100001111000011110000UL
        Assert.Equal(0b0000111100001111000011111111000000110011110011000101010110101010UL, value.rev)
        Assert.Equal(32UL, value.count)
        Assert.Equal(32UL, value.count_dense)
        Assert.Equal(0b0101010110101010001100111100110000000000000000000000000000000000UL, value.diff 0b0000000000000000000000000000000000001111111100001111000011110000UL)
        Assert.True((value.subset 0b1111010110101010001100111100110000001111111100001111000011110000UL))
        Assert.False((value.propersubset 0b0101010110101010001100111100110000001111111100001111000011110000UL))
        Assert.Equal(0b1010110101010001100111100110000001111111100001111000011110000010UL, value.rotateLeft 3)
        Assert.Equal(0b0000101010110101010001100111100110000001111111100001111000011110UL, value.rotateRight 3)
        Assert.Equal(0b1010110101010001100111100110000001111111100001111000011110000010UL, value.rotate 3)
        Assert.Equal(0b0100000000000000000000000000000000000000000000000000000000000000UL, value.leftmost_one)
        Assert.Equal(0b1000000000000000000000000000000000000000000000000000000000000000UL, value.leftmost_zero)
        Assert.Equal(0b0000000000000000000000000000000000000000000000000000000000010000UL, value.rightmost_one)
        Assert.Equal(0b0000000000000000000000000000000000000000000000000000000000000001UL, value.rightmost_zero)
        Assert.Equal(4, value.rightmost_index)
        Assert.Equal(62, value.leftmost_index)
