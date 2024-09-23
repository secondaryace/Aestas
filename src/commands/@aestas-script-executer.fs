//! nuget FsLexYacc=11.3.0
//! fsproj src/commands/compiler/Aestas.Commands.Compiler.fsproj
namespace Aestas.Commands
open System.Collections.Generic
open FSharp.Text.Lexing
open Aestas.Commands.Compiler
open Aestas.Commands.Compiler.Runtime
open Aestas.Prim
open Aestas.Core

module rec AestasScript =
    type AestasScriptCommand = {
        name: string
        description: string
        accessibleDomain: CommandAccessibleDomain
        privilege: CommandPrivilege
        execute: AestasScriptExecuter -> CommandEnvironment -> Context -> Value list -> Context * Value
    }
    let privWrapper cmd executer (env: CommandEnvironment) ctx args =
        if env.privilege < cmd.privilege then
            env.log "Permission denied"; ctx, Tuple []
        else cmd.execute executer env ctx args
    type AestasScriptExecuter(commands') =
        inherit CommandExecuter<AestasScriptCommand>(commands')
        let commands = Dictionary<string, AestasScriptCommand>()
        do
            commands' |> List.iter (fun c -> commands.Add(c.name, c))
        override _.Commands = arrList commands.Values
        override _.AddCommand cmd =
            commands.Add(cmd.name, cmd)
        member val Context = { 
            shared = LanguagePrimitives.operators
            binds = Map.empty
            args = Map.empty
            print = ignore
            trace = [] 
            } with get, set
        override this.Execute env cmd =
            try
                let binds =
                    Seq.fold (fun map cmd -> 
                        map |>
                        Map.change cmd.name (fun _ -> privWrapper cmd this env |> ExternFunction |> Some))
                        this.Context.binds commands.Values
                let parse code = 
                    let lexbuf = LexBuffer<char>.FromString code
                    try
                        let res = Parser.parse Lexer.read lexbuf
                        res
                    with e -> failwithf "parse error: %A\nat: (%d, %d), %A, %A" e.Message lexbuf.EndPos.Line lexbuf.EndPos.Column lexbuf.Lexeme (Lexer.read lexbuf)
                let ast = cmd.Trim() |> parse
                let ctx, ret = run {this.Context with binds = binds; print = env.log; trace = []} ast
                this.Context <- ctx 
                //Logger.logInfo[0] (sprintf "ctx: %A" ctx)
                match ret with
                | Tuple [] -> ()
                | x -> env.log x.Print
            with 
            | AestasScriptException(msg, trace) -> 
                sprintf "Error: %s\nTrace: %s" msg (trace |> String.concat "\n") |> env.log
            | e -> sprintf "Error: %s" e.Message |> env.log