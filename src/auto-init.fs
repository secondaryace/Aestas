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
    let protocols = Dictionary<string, IProtocolAdapter>()
    let bots = arrList<AestasBot>()
    let mappingContentCtors = Dictionary<string, MappingContentCtor*string>()
    let protocolContentCtors = Dictionary<string, ProtocolSpecifyContentCtor*string>()
    let commands = Dictionary<string, ICommand>()

    let init () =
        logInfo["AutoInit"] "Initializing"
        if Directory.Exists "extensions" then
            Directory.GetFiles "extensions"
            |> Array.filter (fun x -> x.EndsWith ".dll")
            |> Array.iter (fun x -> 
                try
                Path.Combine(Environment.CurrentDirectory, x) |> Assembly.LoadFile |> ignore
                logInfo["AutoInit"] $"Loaded assembly {x}"
                with ex -> logError["AutoInit"] $"Error loading assembly {x} {ex}")
        let types = 
            AppDomain.CurrentDomain.GetAssemblies()
            |> Array.collect (fun a -> a.GetTypes())
            |> Array.groupBy (function
                | x when x.IsAbstract -> "noUse"
                | x when x |> implInterface<IAutoInit<AestasBot, unit>> -> "bot"
                | x when x |> implInterface<IAutoInit<(ContentParam->IProtocolSpecifyContent)*string*string, unit>> -> "protocolExt"
                | x when x |> implInterface<IAutoInit<(ContentParam->IAestasMappingContent)*string*string, unit>> -> "pluginAny"
                | x when x |> implInterface<IAutoInit<IProtocolAdapter*string, unit>> -> "protocol"
                | x when x |> implInterface<IAutoInit<ICommand, unit>> -> "command"
                | _ -> "noUse") 
            |> dict
        commands.Append Builtin.commands
        logInfo["AutoInit"] $"""Imported builtin commands: {Builtin.commands.Keys |> String.concat ", "}"""
        if types.ContainsKey "protocol" then 
            types["protocol"] 
            |> Array.iter (fun x -> 
                try
                let p, s = x.GetInterfaceMap(typeof<IAutoInit<IProtocolAdapter*string, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> IProtocolAdapter*string
                protocols.Add(s, p)
                logInfo["AutoInit"] $"Initialized protocol {p.GetType().Name} with name {s}"
                with ex -> logError["AutoInit"] $"Error initializing protocol {toString x} {ex}")
        if types.ContainsKey "pluginAny" then
            types["pluginAny"]
            |> Array.iter (fun x -> 
                try
                let ctor, name, tip = x.GetInterfaceMap(typeof<IAutoInit<(ContentParam->IAestasMappingContent)*string*string, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> (ContentParam->IAestasMappingContent)*string*string
                mappingContentCtors.Add(name, (ctor, tip))
                logInfo["AutoInit"] $"Initialized mappingContentCto with name {name}"
                with ex -> logError["AutoInit"] $"Error initializing mappingContentCto {toString x} {ex}") 
        if types.ContainsKey "protocolExt" then
            types["protocolExt"]
            |> Array.iter (fun x -> 
                try
                let ctor, name, tip = x.GetInterfaceMap(typeof<IAutoInit<(ContentParam->IProtocolSpecifyContent)*string*string, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> (ContentParam->IProtocolSpecifyContent)*string*string
                protocolContentCtors.Add(name, (ctor, tip))
                logInfo["AutoInit"] $"Initialized protocolContentCtor with name {name}"
                with ex -> logError["AutoInit"] $"Error initializing protocolContentCtor {toString x} {ex}")
        if types.ContainsKey "command" then
            types["command"]
            |> Array.iter (fun x -> 
                try
                let cmd = x.GetInterfaceMap(typeof<IAutoInit<ICommand, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> ICommand
                commands.Add(cmd.Name, cmd)
                logInfo["AutoInit"] $"Initialized command {cmd.Name}"
                with ex -> logError["AutoInit"] $"Error initializing command {toString x} {ex}")
        if types.ContainsKey "bot" then
            types["bot"] 
            |> Array.iter (fun x -> 
                try
                let bot = x.GetInterfaceMap(typeof<IAutoInit<AestasBot, unit>>).TargetMethods[0].Invoke(null, [|null|]) :?> AestasBot
                bots.Add(bot)
                logInfo["AutoInit"] $"Initialized bot {bot.Name}"
                with ex -> logError["AutoInit"] $"Error initializing bot {toString x} {ex}")
        logInfo["AutoInit"] "Initialized"