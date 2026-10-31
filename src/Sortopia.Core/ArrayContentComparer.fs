namespace Sortopia.Core

open System.Collections.Generic

/// Compare array keys by their elements. Keys must remain immutable while stored.
type ArrayContentComparer<'T when 'T : equality>() =
    interface IEqualityComparer<'T[]> with
        member _.Equals(left, right) = LanguagePrimitives.GenericEquality left right
        member _.GetHashCode(values) = LanguagePrimitives.GenericEqualityComparer.GetHashCode(values)
