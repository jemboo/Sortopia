namespace Sortopia.Models.Sorting.Serialization

open Sortopia.Sorting.Networks

module internal PrefixDto =
    let encode (prefix: ce array) = prefix |> Array.map Ce.toIndex

    /// Older records have no prefix field. Treat its default null value as an empty prefix.
    let decode (codes: int array) =
        if isNull codes then [||] else codes |> Array.map Ce.fromIndex
