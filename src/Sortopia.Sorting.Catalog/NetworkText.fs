namespace Sortopia.Sorting.Catalog

open System
open System.Globalization
open Sortopia.Sorting.Networks

/// Parser for a sequence of stages: [(0,1),(2,3)] [(1,2)]. Every character must belong to the grammar.
module internal NetworkText =
    let parse (text: string) : ce[][] =
        if isNull text then nullArg "text"
        let mutable position = 0
        let skipWhitespace () =
            while position < text.Length && Char.IsWhiteSpace text.[position] do
                position <- position + 1
        let fail () = raise (FormatException($"Invalid network text at character {position}."))
        let expect character =
            skipWhitespace ()
            if position >= text.Length || text.[position] <> character then fail ()
            position <- position + 1
        let number () =
            skipWhitespace ()
            let start = position
            while position < text.Length && text.[position] >= '0' && text.[position] <= '9' do
                position <- position + 1
            if start = position then fail ()
            Int32.Parse(text.AsSpan(start, position - start), NumberStyles.None, CultureInfo.InvariantCulture)
        let pair () =
            expect '('
            let low = number ()
            expect ','
            let high = number ()
            expect ')'
            if low >= high then raise (FormatException("Comparator endpoints must be strictly increasing."))
            ce.create low high

        let stages = ResizeArray<ce[]>()
        skipWhitespace ()
        while position < text.Length do
            expect '['
            let comparators = ResizeArray<ce>()
            skipWhitespace ()
            if position < text.Length && text.[position] <> ']' then
                comparators.Add(pair ())
                skipWhitespace ()
                while position < text.Length && text.[position] = ',' do
                    expect ','
                    comparators.Add(pair ())
                    skipWhitespace ()
            expect ']'
            stages.Add(comparators.ToArray())
            skipWhitespace ()
        stages.ToArray()
