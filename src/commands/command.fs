namespace Aestas.Commands
open System.Collections.Generic
open System
open System.Reflection
open Lagrange.Core
open Lagrange.Core.Message
open Aestas
open AestasTypes
open Lexer
open Parser
module Command =
    type AestasCommandDomain = None = 0 | Private = 1 | Group = 2 | All = 3
    type AestasCommandAttribute =
        inherit System.Attribute
        val Name: string
        val Domain: AestasCommandDomain
        new(name, domain) = { Name = name; Domain = domain }
    let getCommands filter =
        let ret = Dictionary<string, ICommand>()
        (fun (t: Type) ->
            t.GetCustomAttributes(typeof<AestasCommandAttribute>, false)
            |> Array.iter (fun attr ->
                if attr :?> AestasCommandAttribute |> filter then
                    let command = Activator.CreateInstance(t) :?> ICommand
                    let name = (attr :?> AestasCommandAttribute).Name
                    ret.Add(name, command)
            )
        )|> Array.iter <| Assembly.GetExecutingAssembly().GetTypes()
        ret
    let keywords = readOnlyDict [
        "print", TokenPrint
    ]
    let symbols = readOnlyDict [
        "<|", TokenLeftPipe
        "|>", TokenRightPipe
        "<-", TokenLeftArrow
        "->", TokenRightArrow
        ":", TokenColon
        ";", TokenSemicolon
        "(", TokenLeftRound
        ")", TokenRightRound
        "[", TokenLeftSquare
        "]", TokenRightSquare
        "{", TokenLeftCurly
        "}", TokenRightCurly
        ".", TokenDot
        ",", TokenComma
        "|", TokenPipe
    ]
    let newLine = [|'\n';'\r';'`'|]
    let rec private excecuteAst (env: CommandEnvironment) (ast: Ast) =
        match ast with
        | Tuple items ->
            let rec go acc = function
            | Call h::t ->
                go (excecuteAst env (Call h)::acc) t
            | Tuple h::t ->
                go (excecuteAst env (Tuple h)::acc) t
            | Atom h::t ->
                go (h::acc) t
            | [] -> acc |> List.rev |> AtomTuple
            go [] items
        | Call args ->
            let func = args.Head
            let args = List.map (fun x -> excecuteAst env x) args.Tail
            match excecuteAst env func with
            | Identifier name ->
                if env.commands.ContainsKey name then
                    env.commands[name].Execute(env, args)
                else
                    env.log <| $"Command not found: {name}"
                    Unit
            | x -> 
                env.log <| $"Expected identifier, but found {x}"
                Unit
        | Atom x -> x
    let excecute (env: CommandEnvironment) (cmd: string) =
        let tokens = scanWithoutMacro (makeLanguagePack keywords symbols newLine) cmd
        let ast, _, errors = parse tokens []
        printfn "%A,%A,%A" tokens ast errors
        match errors with
        | [] -> 
            match excecuteAst env ast with
            | Unit -> ()
            | x -> env.log <| x.ToString()
        | _ -> env.log <| String.Join("\n", "Error occured:"::errors)