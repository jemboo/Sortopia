namespace Sortopia.Core.Serialization

open System
open MessagePack
open MessagePack.FSharp
open MessagePack.Resolvers

/// Shared wire options for GeneSort-compatible DTO records and F# unions.
module MessagePackSetup =
    let options =
        let resolver = CompositeResolver.Create(FSharpResolver.Instance, ContractlessStandardResolver.Instance)
        MessagePackSerializerOptions.Standard.WithResolver(resolver).WithSecurity(MessagePackSecurity.UntrustedData)

    /// Encode a DTO using the Sortopia wire contract.
    let serialize (dto: 'T) = MessagePackSerializer.Serialize<'T>(dto, options = options)

    /// Decode a DTO. Use its toDomain function to validate domain invariants.
    let deserialize<'T> (bytes: byte[]) =
        MessagePackSerializer.Deserialize<'T>(ReadOnlyMemory<byte>(bytes), options)

