namespace Sortopia.Core

open System
open System.IO
open System.Security.Cryptography

type IStableSerializable =
    abstract member WriteStableBytes : BinaryWriter -> unit

module GuidUtils = 

    let private MARKER_SERIALIZABLE = 0x01uy
    let private MARKER_STRING       = 0x02uy
    let private MARKER_INT          = 0x03uy
    let private MARKER_GUID         = 0x04uy
    let private MARKER_BOOL         = 0x05uy
    let private MARKER_INT64        = 0x06uy
    let private MARKER_NULL         = 0x07uy

    /// Content ID from the first 16 SHA-256 bytes in .NET Guid byte order.
    let guidFromBytes (bytes: byte[]) = Guid(SHA256.HashData(bytes).AsSpan(0, 16))

    /// Canonical IEEE double for stable rate identities. Equal signed zeros encode identically.
    let writeStableFloat (writer: BinaryWriter) (value: float) =
        writer.Write(if value = 0.0 then 0.0 else value)

    let writeStableValue (writer: BinaryWriter) (value: #IStableSerializable) =
        value.WriteStableBytes(writer)

    let guidFromObjs (values: obj seq) =
        use stream = new MemoryStream()
        use writer = new BinaryWriter(stream)

        values |> Seq.iter (fun o -> 
            match o with
            | :? IStableSerializable as serializable -> 
                writer.Write(MARKER_SERIALIZABLE)
                serializable.WriteStableBytes(writer)
            | :? string as s -> 
                writer.Write(MARKER_STRING)
                writer.Write(s) 
            | :? int as i -> 
                writer.Write(MARKER_INT)
                writer.Write(i)
            | :? Guid as g -> 
                writer.Write(MARKER_GUID)
                writer.Write(g.ToByteArray() : byte[]) 
            | :? bool as b ->
                writer.Write(MARKER_BOOL)
                writer.Write(b)
            | :? int64 as i64 ->
                writer.Write(MARKER_INT64)
                writer.Write(i64)
            | null ->
                writer.Write(MARKER_NULL) 
            | _ -> 
                failwithf "Type '%s' cannot be securely hashed." (o.GetType().FullName)
        )
        
        writer.Flush() 
        guidFromBytes (stream.ToArray())
