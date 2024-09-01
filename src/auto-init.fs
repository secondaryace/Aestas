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
    let inline getMappingContentCtorTip<'t when 't :> IAestasMappingContent> () =
        if mappingContentCtorTips.ContainsKey typeof<'t> then mappingContentCtorTips[typeof<'t>] else failwith $"MappingContentCtor {toString typeof<'t>} not found"
    let inline getProtocolContentCtorTip<'t when 't :> IProtocolSpecifyContent> () =
        if protocolContentCtorTips.ContainsKey typeof<'t> then protocolContentCtorTips[typeof<'t>] else failwith $"ProtocolContentCtor {toString typeof<'t>} not found"
    let init () =
        logInfo["AutoInit"] "Initializing"
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
                100, fun () ->
                    let bot = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized bot {(bot: AestasBot).Name}"
                    _bots.Add bot
            | [|t; targ|] when t = typeof<(ContentParam->IProtocolSpecifyContent)*string*(AestasBot->string)> && targ = typeof<unit> -> 
                1, fun () ->
                    let ctor, contentType, tip = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized content plugin {toString t'}"
                    _protocolContentCtorTips.Add(t', (ctor, contentType, tip))
            | [|t; targ|] when t = typeof<(ContentParam->IAestasMappingContent)*string*(AestasBot->string)> && targ = typeof<unit> -> 
                1, fun () ->
                    let ctor, contentType, tip = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized content plugin {toString t'}"
                    _mappingContentCtorTips.Add(t', (ctor, contentType, tip))
            | [|t; targ|] when t = typeof<IProtocolAdapter> && targ = typeof<unit> -> 
                0, fun () ->
                    let protocol = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized protocol {toString t'}"
                    _protocols.Add(t', protocol)
            | [|t; targ|] when t = typeof<ICommand> && targ = typeof<unit> -> 
                2, fun () ->
                    let cmd = invokeInit t' ()
                    logInfo["AutoInit"] $"Initialized command {toString t'}"
                    _commands.Add(t', cmd)
            | _ -> 101, ignore)
        |> Array.sortBy fst
        |> Array.iter (fun (p, f) -> if p <> 101 then try f() with ex -> logError["AutoInit"] $"Error initializing {ex}")
        // if types.ContainsKey "protocol" then 
        //     types["protocol"] 
        //     |> Array.iter (fun (_, i, x, t, targ) -> 
        //         try
        //         let p = x.GetInterfaceMap(typeof<IAutoInit<IProtocolAdapter, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> IProtocolAdapter
        //         protocols.Add(t, p)
        //         logInfo["AutoInit"] $"Initialized protocol {p.GetType().Name}"
        //         with ex -> logError["AutoInit"] $"Error initializing protocol {toString x} {ex}")
        // if types.ContainsKey "pluginAny" then
        //     types["pluginAny"]
        //     |> Array.iter (fun x -> 
        //         try
        //         let ctor, name, tip = x.GetInterfaceMap(typeof<IAutoInit<(ContentParam->IAestasMappingContent)*string*string, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> (ContentParam->IAestasMappingContent)*string*string
        //         mappingContentCtors.Add(name, (ctor, tip))
        //         logInfo["AutoInit"] $"Initialized mappingContentCto with name {name}"
        //         with ex -> logError["AutoInit"] $"Error initializing mappingContentCto {toString x} {ex}") 
        // if types.ContainsKey "protocolExt" then
        //     types["protocolExt"]
        //     |> Array.iter (fun x -> 
        //         try
        //         let ctor, name, tip = x.GetInterfaceMap(typeof<IAutoInit<(ContentParam->IProtocolSpecifyContent)*string*string, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> (ContentParam->IProtocolSpecifyContent)*string*string
        //         protocolContentCtors.Add(name, (ctor, tip))
        //         logInfo["AutoInit"] $"Initialized protocolContentCtor with name {name}"
        //         with ex -> logError["AutoInit"] $"Error initializing protocolContentCtor {toString x} {ex}")
        // if types.ContainsKey "command" then
        //     types["command"]
        //     |> Array.iter (fun x -> 
        //         try
        //         let cmd = x.GetInterfaceMap(typeof<IAutoInit<ICommand, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> ICommand
        //         commands.Add(cmd.Name, cmd)
        //         logInfo["AutoInit"] $"Initialized command {cmd.Name}"
        //         with ex -> logError["AutoInit"] $"Error initializing command {toString x} {ex}")
        // if types.ContainsKey "bot" then
        //     types["bot"] 
        //     |> Array.iter (fun x -> 
        //         try
        //         let bot = x.GetInterfaceMap(typeof<IAutoInit<AestasBot, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> AestasBot
        //         bots.Add(bot)
        //         logInfo["AutoInit"] $"Initialized bot {bot.Name}"
        //         with ex -> logError["AutoInit"] $"Error initializing bot {toString x} {ex}")
        logInfo["AutoInit"] "Initialized"