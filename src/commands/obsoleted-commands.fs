namespace Aestas.Commands
open System.Text
open Aestas.Prim
open Aestas.Core
open Aestas.Commands.ObsoletedCommand

module ObsoletedCommands =
    type IdentityCommand() =
        interface ICommand with
            member _.Name = "id"
            member _.Help = "Operator: x ... -> x"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                match args with
                | [] -> Unit
                | [x] -> x
                | _ -> env.log "Expected one argument"; Unit
    type MakeTupleCommand() =
        interface ICommand with
            member _.Name = "tuple"
            member _.Help = "Operator: x y ... -> (x, y, ...)"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                args |> AtomTuple
    type EqualCommand() = //not yet implemented
        interface ICommand with
            member _.Name = "eq"
            member _.Help = "Operator: eq x y -> 1 if x = y, 0 otherwise"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                match args with
                | x::y::[] -> 
                    match x, y with
                    | Number x, Number y -> if x = y then Number 1. else Number 0.
                    | String x, String y -> if x = y then Number 1. else Number 0.
                    | Identifier x, Identifier y -> if x = y then Number 1. else Number 0.
                    | _, _ -> env.log "Expected same type"; Number 0.
                | _ -> env.log "Expected two arguments"; Number 0.
    type ProjectionCommand() =
        interface ICommand with
            member _.Name = "proj"
            member _.Help = "Operator: proj \"a\" ({a: x; b: y, ...}, {a: z; b: w, ...}, ...) -> (x, z, ...)"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                match args with
                | Identifier x::AtomTuple ls::[] -> 
                    let rec go acc = function
                    | [] -> acc |> List.rev
                    | AtomObject o::t -> 
                        match o |> Map.tryFind x with
                        | Some v -> go (AtomObject(Map [x, v])::acc) t
                        | None -> env.log $"Key {x} not found"; go (Unit::acc) t
                    | _ -> env.log "Expected object"; go (Unit::acc) []
                    go [] ls |> AtomTuple
                | AtomTuple xs::AtomTuple ls::[] ->
                    let rec go acc = function
                    | [] -> acc |> List.rev
                    | AtomObject o::t -> 
                        ((xs 
                        |> List.map (function
                            | Identifier x -> 
                                match o |> Map.tryFind x with
                                | Some v -> x, v
                                | None -> env.log $"Key {x} not found"; "_", Unit
                            | _ -> env.log "Expected identifier"; "_", Unit)
                        |> Map |> AtomObject)::acc |> go) t
                    | _ -> env.log "Expected object"; go (Unit::acc) []
                    go [] ls |> AtomTuple
                | _ -> env.log "Expected identifier and object tuple"; Unit
    type DummyIfCommand() =
        interface ICommand with
            member _.Name = "if"
            member _.Help = "Operator: if 0 x y -> y, if 1 x y -> x"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                args |> AtomTuple
    type VersionCommand() =
        interface ICommand with
            member _.Name = "version"
            member _.Help = "Print the version of Aestas"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                env.log $"Aestas version {version}"; Unit
    type DomainInfoCommand() =
        interface ICommand with
            member _.Name = "domaininfo"
            member _.Help = "Print the current domain info"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                Map [
                    "id", env.domain.DomainId |> float |> Number
                    "name", env.domain.Name |> String
                    "private", if env.domain.Private then Number 1. else Number 0.
                ] |> AtomObject
    type ListDomainCommand() =
        interface ICommand with
            member _.Name = "lsdomain"
            member _.Help = "List all domains"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                let sb = StringBuilder()
                env.bot.Domains |>
                Seq.map (fun p ->
                    Map [
                        "id", p.Value.DomainId |> float |> Number
                        "name", p.Value.Name |> String
                        "private", if p.Value.Private then Number 1. else Number 0.
                    ] |> AtomObject)
                |> List.ofSeq
                |> AtomTuple
    type HelpCommand() =
        interface ICommand with
            member _.Name = "help"
            member _.Help = "List all commands"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                match
                    env.bot.CommandExecuters 
                    |> Dict.tryFind (fun k v -> v.GetType() = typeof<ObsoletedCommandExeuter>)
                with
                | Some (_, v) -> 
                    let v = v :?> ObsoletedCommandExeuter
                    let sb = StringBuilder()
                    sb.Append "## Commands" |> ignore
                    v.Commands |>
                    Dict.iter (fun _ v -> sb.Append $"\n* {v.Name}:\n   {v.Help}" |> ignore)
                    sb.ToString() |> env.log
                | _ -> ()
                Unit
    type DumpCommand() =
        interface ICommand with
            member _.Name = "dump"
            member _.Help = """Dump the cached context, the input index is from the end of the context | Usage: dump [from=count-1] [to=0]"""
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal
            member _.Execute env args =
                let msgs = env.domain.Messages
                let args, _ = args |> CommandHelper.parseArguments [
                    CommandHelper.ParamNumber("from", msgs.Count-1 |> float |> Some)
                    CommandHelper.ParamNumber("to", Some 0.)
                ]
                let s, t =
                    match args["from"], args["to"] with
                    | CommandHelper.ArgNumber x, CommandHelper.ArgNumber y -> msgs.Count-1-int x, msgs.Count-1-int y
                    | _ -> failwith "Should not happen"
                let sb = StringBuilder()
                sb.Append "## Cached Context:" |> ignore
                let rec go i =
                    if i > t then ()
                    else
                        let m = msgs[i].Parse()
                        sb.Append $"\n* {m.sender.name}:\n   {m.content}\n  ({m.mid})" |> ignore
                        go (i+1)
                go s
                sb.ToString() |> env.log; Unit
    type ClearCommand() =
        interface ICommand with
            member _.Name = "clear"
            member _.Help = "Clear the cached context"
            member _.AccessibleDomain = CommandAccessibleDomain.All
            member _.Privilege = CommandPrivilege.Normal                
            member _.Execute env args =
                env.bot.ClearCachedContext env.domain
                env.log "Cached context cleared"; Unit
    let operators() = Map.ofList [
        "id", IdentityCommand() :> ICommand
        "tuple", MakeTupleCommand()
        "proj", ProjectionCommand()
        "if", DummyIfCommand()
    ]
    let commands() =  Map.ofList [
        "version", VersionCommand() :> ICommand
        "domaininfo", DomainInfoCommand()
        "lsdomain", ListDomainCommand()
        "help", HelpCommand()
        "dump", DumpCommand()
        "clear", ClearCommand()
    ]