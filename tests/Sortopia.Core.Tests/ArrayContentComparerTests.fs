namespace Sortopia.Core.Tests

open System.Collections.Generic
open Xunit
open Sortopia.Core

module ArrayContentComparerTests =
    [<Fact>]
    let ``sets deduplicate equal output sequences in separate buffers`` () =
        let keys = HashSet<int[]>(ArrayContentComparer<int>())
        Assert.True(keys.Add([| 1; 3; 2 |]))
        Assert.False(keys.Add([| 1; 3; 2 |]))
        Assert.True(keys.Add([| 3; 1; 2 |]))
