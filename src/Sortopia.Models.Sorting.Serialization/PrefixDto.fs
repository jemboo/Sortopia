namespace Sortopia.Models.Sorting.Serialization

open Sortopia.Sorting.Networks

module internal PrefixDto =
    let encode (prefix: ce array) = prefix |> Array.map Ce.toIndex

    let decode (codes: int array) =
        if isNull codes then invalidArg "prefixCeCodes" "A saved model must include its prefix; use an empty array for no prefix."
        codes |> Array.map Ce.fromIndex
