namespace Sortopia.Sorting.Serialization.Inputs

open MessagePack

/// DTO for an individual SIMD block
[<MessagePackObject>]
type simdSortBlockDto = {
    /// Vectors flattened: each Vector256<uint8> becomes a 32-byte array
    [<Key(0)>] RawVectors: byte[][] 
    [<Key(1)>] SortableCount: int
}
