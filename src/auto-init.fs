namespace Aestas
open System
open System.IO
open System.Collections.Generic
open System.Reflection
open Aestas.Prim
open Aestas.Core
open Aestas.Core.Logger

/// AutoInit is a type-safe auto initializer.
/// It will scan all types in the current app domain and initialize all types that implement IAutoInit.
/// <summary>Impl IAutoInit<AestasBot, unit> -> bot</summary>
/// <summary>Impl IAutoInit<(ContentParam->IProtocolSpecifyContent)*string*string, unit> -> protocolContentCtors</summary>
/// <summary>Impl IAutoInit<(ContentParam->IAestasMappingContent)*string*string, unit> -> mappingContentCtors</summary>
/// <summary>Impl IAutoInit<IProtocolAdapter*string, unit> -> protocols</summary>
/// <summary>Impl IAutoInit<ICommand, unit> -> commands</summary>
module AutoInit =
    type IAutoInit<'t, 'tArg> =
        static abstract member Init: 'tArg -> 't
    let private _protocols = Dictionary<Type, IProtocolAdapter>()
    let private _bots = arrList<AestasBot>()
    let private _mappingContentCtorTips = Dictionary<Type, MappingContentCtor*string*(AestasBot->string)>()
    let private _protocolContentCtorTips = Dictionary<Type, ProtocolSpecifyContentCtor*string*(AestasBot->string)>()
    let private _commands = Dictionary<Type, ICommand>()
    let bots = _bots :> IReadOnlyList<AestasBot>
    let protocols = _protocols :> IReadOnlyDictionary<Type, IProtocolAdapter>
    let mappingContentCtorTips = _mappingContentCtorTips :> IReadOnlyDictionary<Type, MappingContentCtor*string*(AestasBot->string)>
    let protocolContentCtorTips = _protocolContentCtorTips :> IReadOnlyDictionary<Type, ProtocolSpecifyContentCtor*string*(AestasBot->string)>
    let commands = _commands :> IReadOnlyDictionary<Type, ICommand>
    let inline invokeInit<'t, 'tArg> (t: Type) (arg: 'tArg) =
        t.GetInterfaceMap(typeof<IAutoInit<'t, 'tArg>>).TargetMethods[0].Invoke(null, [|arg|]) :?> 't
    let inline tryGetCommand s =
        match commands |> Dict.tryFind (fun k v -> k.Name = s) with
        | Some v -> Some v
        | None -> None
    let inline tryGetProtocol s =
        match protocols |> Dict.tryFind (fun k v -> k.GetType().Name = s) with
        | Some v -> Some v
        | None -> None
    let inline tryGetMappingContentCtorTip s =
        match mappingContentCtorTips |> Dict.tryFind (fun k v -> k.Name = s) with
        | Some v -> Some v
        | None -> None
    let inline tryGetProtocolContentCtorTip s =
        match protocolContentCtorTips |> Dict.tryFind (fun k v -> k.Name = s) with
        | Some v -> Some v
        | None -> None
    let inline getCommand<'t when 't :> ICommand> () = 
        if commands.ContainsKey typeof<'t> then commands[typeof<'t>] else failwith $"Command {toString typeof<'t>} not found"
    let inline getProtocol<'t when 't :> IProtocolAdapter> () =
        if protocols.ContainsKey typeof<'t> then protocols[typeof<'t>] else failwith $"Protocol {toString typeof<'t>} not found"
    let inline getContentParser<'t when 't :> IAutoInit<string*MappingContentCtor*(AestasBot -> string), unit>> () =
        if mappingContentCtorTips.ContainsKey typeof<'t> then mappingContentCtorTips[typeof<'t>] else failwith $"ContentParser {toString typeof<'t>} not found"
    let inline getProtocolContentCtorTip<'t when 't :> IProtocolSpecifyContent> () =
        if protocolContentCtorTips.ContainsKey typeof<'t> then protocolContentCtorTips[typeof<'t>] else failwith $"ProtocolContentCtor {toString typeof<'t>} not found"
    type InitTypes =
        | Ignore = 101
        | Bot = 100
        | Protocol = 0
        | ProtocolPlugin = 1
        | ContentParser = 2
        | Command = 3
    let mutable _initializers: (InitTypes * (unit -> unit)) array option = None
    let _init (initTypes: Set<InitTypes>) =
        logInfo["AutoInit"] "Initializing"
        if Set.contains InitTypes.Ignore initTypes then failwith "Ignore is not allowed in initTypes"
        if Directory.Exists "extensions" then
            Directory.GetFiles "extensions"
            |> Array.filter (fun x -> x.EndsWith ".dll")
            |> Array.iter (fun x -> 
                try
                Path.Combine(Environment.CurrentDirectory, x) |> Assembly.LoadFile |> ignore
                logInfo["AutoInit"] $"Loaded assembly {x}"
                with ex -> logError["AutoInit"] $"Error loading assembly {x}, {ex}")
        Builtin.commands |> Dict.iter (fun k v -> _commands.Add(v.GetType(), v))
        logInfo["AutoInit"] $"""Imported builtin commands: {_commands.Keys |> Seq.map (fun x -> x.Name) |> String.concat ", "}"""
        AppDomain.CurrentDomain.GetAssemblies()
        |> Array.collect (fun a -> a.GetTypes())
        |> Array.choose (fun t -> 
            t.GetInterfaces()
            |> Array.map (fun i -> i, t)
            |> Array.tryFind (fun (i, t) ->
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() = typedefof<IAutoInit<_, _>>))
        |> Array.map (fun (i, t') ->
            match i.GenericTypeArguments with
            | [|t; targ|] when t = typeof<AestasBot> && targ = typeof<unit> -> 
                InitTypes.Bot, fun () ->
                    let bot = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized bot {(bot: AestasBot).Name}"
                    _bots.Add bot
            | [|t; targ|] when t = typeof<string*ProtocolSpecifyContentCtor*(AestasBot -> string)> && targ = typeof<unit> -> 
                InitTypes.ProtocolPlugin, fun () ->
                    let name, ctor, tip = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized content plugin {toString t'}"
                    _protocolContentCtorTips.Add(t', (ctor, name, tip))
            | [|t; targ|] when t = typeof<string*MappingContentCtor*(AestasBot -> string)> && targ = typeof<unit> -> 
                InitTypes.ContentParser, fun () ->
                    let name, ctor, tip = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized content plugin {toString t'}"
                    _mappingContentCtorTips.Add(t', (ctor, name, tip))
            | [|t; targ|] when t = typeof<IProtocolAdapter> && targ = typeof<unit> -> 
                InitTypes.Protocol, fun () ->
                    let protocol = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized protocol {toString t'}"
                    _protocols.Add(t', protocol)
            | [|t; targ|] when t = typeof<ICommand> && targ = typeof<unit> -> 
                InitTypes.Command, fun () ->
                    let cmd = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized command {toString t'}"
                    _commands.Add(t', cmd)
            | _ -> InitTypes.Ignore, ignore)
        |> Array.sortBy fst
        |> Array.iter (fun (p, f) -> if p <> InitTypes.Ignore && Set.contains p initTypes then try f() with ex -> logError["AutoInit"] $"Error initializing {ex}")
        logInfo["AutoInit"] "Initialized"
    let init force (initTypes: Set<InitTypes>) =
        match force, _initializers with
        | true, _ -> 
            _bots.Clear()
            _protocols.Clear()
            _commands.Clear()
            _mappingContentCtorTips.Clear()
            _protocolContentCtorTips.Clear()
            _init initTypes
        | _, None -> _init initTypes
        | _ -> ()
    let initAll () = 
        seq {InitTypes.Bot; InitTypes.Protocol; InitTypes.ProtocolPlugin; InitTypes.ContentParser; InitTypes.Command}
        |> Set.ofSeq |> init true