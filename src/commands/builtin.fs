namespace Aestas.Commands
open System.Text
open Lagrange.Core.Message
open Aestas
open Aestas.ChatApi
open Command
open Prim
open AestasTypes
module AestasBuiltinCommands =
    let private isPrivate (chain: MessageChain) = 
        chain.GroupUin.HasValue |> not
    [<AestasCommand("id", AestasCommandDomain.All)>]
    type Identity() =
        interface ICommand with
            member this.Execute (env, args) =
                args[0]
            member this.Help = "Identity command"
    [<AestasCommand("conslog", AestasCommandDomain.Chat)>]
    type ConsoleLog() =
        interface ICommand with
            member this.Execute (env, args) =
                args[0]//Dummy
            member this.Help = "Redirects output to console"
    [<AestasCommand("version", AestasCommandDomain.All)>]
    type Version() =
        interface ICommand with
            member this.Execute (env, args) =
                env.log $"Aestas version v{version}"; Unit
            member this.Help = "Prints the version of Aestas"
    [<AestasCommand("help", AestasCommandDomain.All)>]
    type Help() =
        interface ICommand with
            member this.Execute (env, args) =
                let sb = StringBuilder()
                sb.Append("Commands:") |> ignore
                for p in env.commands do
                    sb.Append("\n#").Append(p.Key).Append(":\n--").Append(p.Value.Help) |> ignore
                sb.ToString() |> env.log; Unit
            member this.Help = "Prints the commands"
    [<AestasCommand("current", AestasCommandDomain.Chat)>]
    type Current() =
        interface ICommand with
            member this.Execute (env, args) =
                env.log $"Current model is {env.model.Value.GetType()}"; Unit
            member this.Help = "Prints the current model"
    [<AestasCommand("switch", AestasCommandDomain.Chat)>]
    type Switch() =
        interface ICommand with
            member this.Execute (env, args) =
                let profile x = 
                    if isPrivate env.chain then $"profiles/chat_info_private_{x}.json" else $"profiles/chat_info_group_{x}.json"
                let inline change model = env.model.Value <- model;env.log $"Model switched to \"{model.GetType()}\"";
                match args with
                | Identifier "help"::[] ->
                    env.log "Usage: switch [list] [modelName: string]"
                | Identifier "list"::[] ->
                    env.log "Available models: unit, gemini, geminif, gemini10, ernie35, ernie35p, ernie40, ernie40p, erniechara, cohere"
                | String name::[]
                | Identifier name::[] ->
                    let model = 
                        match name with
                        | "unit" -> UnitClient() :> IChatClient
                        | "gemini" -> IChatClient.Create<GeminiClient>[|profile "gemini"; false|]
                        | "geminif" -> IChatClient.Create<GeminiClient>[|profile "gemini"; true|]
                        | "gemini10" -> IChatClient.Create<Gemini10Client>[|profile "gemini"; ""|]
                        | "ernie35" -> IChatClient.Create<ErnieClient>[|profile "ernie"; Ernie_35|]
                        | "ernie35p" -> IChatClient.Create<ErnieClient>[|profile "ernie"; Ernie_35P|]
                        | "ernie40" -> IChatClient.Create<ErnieClient>[|profile "ernie"; Ernie_40|]
                        | "ernie40p" -> IChatClient.Create<ErnieClient>[|profile "ernie"; Ernie_40P|]
                        | "erniechara" -> IChatClient.Create<ErnieClient>[|profile "ernie"; Ernie_Chara|]
                        | "cohere" -> IChatClient.Create<CohereClient>[|profile "cohere"|]
                        | _ -> failwith $"Could not find model {name}"
                    change model
                | _ -> env.log "Invalid arguments";
                Unit
            member this.Help = "Switches the model"
    [<AestasCommand("dump", AestasCommandDomain.Chat)>]
    type Dump() =
        interface ICommand with
            member this.Execute (env, args) =
                let sb = StringBuilder()
                let msgs = env.model.Value.Messages
                let length = 
                    match args with
                    | [] -> msgs.Count
                    | Number n::[] -> int n
                    | _ -> failwith "Invalid arguments"
                sb.Append("Context:") |> ignore
                for i = 0 to length-1 do
                    let m = msgs[msgs.Count-length+i]
                    sb.Append('\n').Append(m.role).Append(": ").Append(m.content) |> ignore
                sb.ToString() |> env.log; Unit
            member this.Help = "Dumps the context"
    [<AestasCommand("awakeme", AestasCommandDomain.Group)>]
    type AwakeMe() =
        interface ICommand with
            member this.Execute (env, args) =
                match args with
                | [] -> env.log "Invalid arguments"
                | Identifier "help"::[] ->
                    env.log "Usage: awakeme [ create | remove | update | list ] [word: string] [chance: number]"
                | Identifier "create"::String s::Number n::[]
                | String s::Number n::[] -> 
                    env.aestas.awakeMe.Add(s, n)
                    env.log $"Added {s} to awake dictionary with {n}"
                | Identifier "create"::String s::[]
                | String s::[] ->
                    env.aestas.awakeMe.Add(s, 1.0f)
                    env.log $"Added {s} to awake dictionary with 1.0"
                | Identifier "remove"::String s::[] ->
                    if env.aestas.awakeMe.ContainsKey s then
                        env.aestas.awakeMe.Remove(s) |> ignore
                        env.log $"Removed {s} from awake dictionary"
                    else env.log $"Could not find {s} in awake dictionary"
                | Identifier "update"::String s::Number n::[] ->
                    if env.aestas.awakeMe.ContainsKey s then
                        env.aestas.awakeMe[s] <- n
                        env.log $"Updated {s} in awake dictionary with {n}"
                    else env.log $"Could not find {s} in awake dictionary"
                | Identifier "list"::[] ->
                    let sb = StringBuilder()
                    sb.Append("Awake words:") |> ignore
                    for p in env.aestas.awakeMe do
                        sb.Append('\n').Append(p.Key).Append(" with chance ").Append(p.Value) |> ignore
                    sb.ToString() |> env.log;
                | _ -> env.log "Invalid arguments"
                Unit
            member this.Help = "Adds or removes a word from the awake dictionary"
    [<AestasCommand("info", AestasCommandDomain.All)>]
    type Info() =
        interface ICommand with
            member this.Execute (env, args) =
                let system = System.Environment.OSVersion.VersionString
                let machine = System.Environment.MachineName
                let cpuCore = System.Environment.ProcessorCount
                let cpuName = 
                    if System.Environment.OSVersion.Platform = System.PlatformID.Unix then
                        (bash "cat /proc/cpuinfo | grep 'model name' | uniq | cut -d ':' -f 2").Trim()
                    else
                        (cmd " wmic cpu get name | find /V \"Name\"").Trim()
                let ipinfo =
                    use web = new System.Net.Http.HttpClient()
                    web.GetStringAsync("http://ip-api.com/line/").Result.Split('\n')
                env.log $"""System Info:
| System: {system}
| Machine: {machine}
| CPU: {cpuName}
| CPU Core: {cpuCore}
| IP: {ipinfo[1]} {ipinfo[4]} {ipinfo[5]}, {ipinfo[11]}"""
                Unit
            member this.Help = "Prints system infos"
    [<AestasCommand("notes", AestasCommandDomain.All)>]
    type Notes() =
        interface ICommand with
            member this.Execute (env, args) =
                match args with
                | Identifier "help"::[] ->
                    env.log "Usage: notes [ clear | print ]"
                | Identifier "clear"::[] ->
                    env.aestas.notes.Clear()
                    env.log "Cleared notes"
                | [] | Identifier "print"::[] ->
                    let sb = StringBuilder()
                    sb.Append("Notebook:") |> ignore
                    for note in env.aestas.notes do
                        sb.Append('\n').Append(note) |> ignore
                    sb.ToString() |> env.log;
                | _ -> env.log "Invalid arguments"
                Unit
            member this.Help = "Examine the notebook"
    [<AestasCommand("op", AestasCommandDomain.All)>]
    type DoOperators() =
        interface ICommand with
            member this.Execute (env, args) =
                match args with
                | Identifier "help"::[] ->
                    env.log "Usage: op [ + | - | * | / ] [x: number] [y: number]"; Unit
                | String "+"::Number x::Number y::[] ->
                    Number (x + y)
                | String "-"::Number x::Number y::[] ->
                    Number (x - y)
                | String "*"::Number x::Number y::[] ->
                    Number (x * y)
                | String "/"::Number x::Number y::[] ->
                    Number (x / y)
                | String "%"::Number x::Number y::[] ->
                    Number (x % y)
                | _ -> env.log "Invalid arguments"; Unit
            member this.Help = "Caculate numbers, just for fun"
    [<AestasCommand("ip", AestasCommandDomain.All)>]
    type Ip() =
        interface ICommand with
            member this.Execute (env, args) =
                let ip = 
                    let ipv6 = (bash "curl 6.ipw.cn")
                    let ipv4 = (bash "curl 4.ipw.cn")
                    ipv4 + "  当前ipv6： " + ipv6
                env.log $"""当前ipv4 ：{ip}"""; Unit
            member this.Help = "Prints the IP address"