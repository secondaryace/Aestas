//! fsproj src/commands/compiler/Aestas.Commands.Compiler.fsproj
namespace Aestas.Commands
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
        execute: AestasScriptExecuter -> CommandEnvironment -> Context -> Value list -> (Context*Value)
    }
    type AestasScriptExecuter(commands) =
        inherit CommandExecuter<AestasScriptCommand>(commands)
        member val Context = { binds = Prim.operators; args = Map.empty; print = ignore } with get, set
        override this.Execute env cmd =
            let binds =
                ArrList.fold (fun map cmd -> 
                    map |>
                    Map.change cmd.name (fun _ -> cmd.execute this env |> FSharpFunction |> Some))
                    this.Context.binds this.Commands
            let parse code = 
                let lexbuf = LexBuffer<char>.FromString code
                try
                    let res = Parser.parse Lexer.read lexbuf
                    res
                with e -> failwithf "parse error: %A\nat: (%d, %d), %A, %A" e.Message lexbuf.EndPos.Line lexbuf.EndPos.Column lexbuf.Lexeme (Lexer.read lexbuf)
            let ast = cmd.Trim() |> parse
            let ctx, ret = run {this.Context with binds = binds; print = env.log} ast
            this.Context <- ctx 
            //Logger.logInfo[0] (sprintf "ctx: %A" ctx)
            match ret with
            | Unit -> ()
            | x -> env.log x.Print