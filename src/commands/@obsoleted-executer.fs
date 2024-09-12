namespace Aestas.Commands
open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open System.Text.RegularExpressions
open System.Linq
open System.Reflection
open Aestas.Prim
open Aestas.Core

module rec ObsoletedCommand =
    type ObsoletedCommandExeuter(commands) =
        inherit CommandExecuter<ICommand>(commands)
        member val Commands: Dictionary<string, ICommand> = Dictionary() with get
        override this.Execute env cmd =
            base.Commands |> ArrList.iter (fun cmd ->
                if this.Commands.ContainsKey cmd.Name then ()
                else this.Commands.Add(cmd.Name, cmd)
            )
            Command.execute this env cmd
        new (cmds: Map<string, ICommand> seq) = new ObsoletedCommandExeuter(cmds |> Seq.map _.Values |> Seq.concat)
    type ICommand =
        abstract member Execute: CommandEnvironment -> Atom list -> Atom
        abstract member Name: string with get
        abstract member Help: string with get
        abstract member AccessibleDomain: CommandAccessibleDomain
        abstract member Privilege: CommandPrivilege
    type Ast =
        | Call of Ast list
        | Tuple of Ast list
        | Atom of Atom
    [<StructuredFormatDisplay("{Display}")>]
    type Atom =
        | AtomTuple of Atom list
        | AtomObject of Map<string, Atom>
        | Number of float
        | String of string
        | Identifier of string
        | Unit
        with
            member this.Display = 
                match this with
                | AtomTuple l -> $"""({l |> List.map (fun x -> x.Display) |> String.concat ", "})"""
                | AtomObject l -> $"""{{
{l 
    |> Map.map (fun k v -> $"  {k} = {v.Display}") |> Map.values |> String.concat "\n"}
}}"""
                | Number n -> n.ToString()
                | String s -> $"\"{s}\""
                | Identifier i -> i
                | Unit -> "()"
    module Command =
        module Lexer =
            type Token =
                | TokenSpace
                | TokenPrint
                | TokenFloat of float
                | TokenString of string
                /// a, bar, 变量 etc.
                | TokenIdentifier of string
                /// '<|'
                | TokenLeftPipe
                /// '|>'
                | TokenRightPipe
                /// '('
                | TokenLeftRound
                /// ')'
                | TokenRightRound
                /// '['
                | TokenLeftSquare
                /// ']'
                | TokenRightSquare
                /// '{'
                | TokenLeftCurly
                /// '}'
                | TokenRightCurly
                /// '<-'
                | TokenLeftArrow
                /// '->'
                | TokenRightArrow
                /// '|'
                | TokenPipe
                | TokenNewLine
                /// '.'
                | TokenDot
                /// ','
                | TokenComma
                /// ';'
                | TokenSemicolon
                /// ':'
                | TokenColon
                /// string: Error message
                | TokenError of string
            type private _stDict = IReadOnlyDictionary<string, Token>
            type Macros = IReadOnlyDictionary<string, string>
            type MaybeDict = 
                | AValue of Token
                | ADict of Dictionary<char, MaybeDict> 
            /// A pack of language primitive informations
            type LanguagePack = {keywords: _stDict; operatorChars: char array; operators: Dictionary<char, MaybeDict>; newLine: char array}
            /// Scan the whole source code
            let scan (lp: LanguagePack) (source: string) (macros: Macros) =
                let rec checkBound (k: string) (s: string) =
                    let refs = s.Split(' ') |> Array.filter (fun s -> s.StartsWith '$')
                    let mutable flag = false
                    for p in macros do
                        flag <- flag || (refs.Contains p.Key && p.Value.Contains k)
                    flag
                let mutable flag = false
                for p in macros do
                    flag <- flag || checkBound p.Key p.Value
                if flag then [TokenError("Macro looped reference detected")]
                else scan_ (arrList()) lp source macros |> List.ofSeq
            let scanWithoutMacro (lp: LanguagePack) (source: string)=
                scan_ (arrList()) lp source (readOnlyDict []) |> List.ofSeq
            let rec private scan_ (tokens: Token arrList) (lp: LanguagePack) (source: string) (macros: Macros) = 
                let cache = StringBuilder()
                let rec innerRec (tokens: Token arrList) cursor =
                    if cursor >= source.Length then tokens else
                    let lastToken = if tokens.Count = 0 then TokenNewLine else tokens[^0]
                    cache.Clear() |> ignore
                    match source[cursor] with
                    | ' ' -> 
                        if (lastToken = TokenNewLine || lastToken = TokenSpace) |> not then tokens.Add(TokenSpace)
                        innerRec tokens (cursor+1)
                    | '\"' ->
                        let (cursor, eof) = scanString lp (cursor+1) source cache
                        if eof then tokens.Add (TokenError "String literal arrives the end of file")
                        else tokens.Add (TokenString (cache.ToString()))
                        innerRec tokens cursor
                    | c when Array.contains c lp.newLine ->
                        if lastToken = TokenNewLine |> not then 
                            if lastToken = Token.TokenSpace then tokens[^0] <- TokenNewLine
                            else tokens.Add(TokenNewLine)
                        innerRec tokens (cursor+1)
                    | c when Array.contains c lp.operatorChars ->
                        if c = '(' && source.Length-cursor >= 2 && source[cursor+1] = '*' then
                            let (cursor, eof) = scanComment lp (cursor+2) source cache
                            if eof then tokens.Add (TokenError "Comment arrives the end of file")
                            innerRec tokens cursor
                        else
                            let cursor = scanSymbol lp cursor source cache
                            let rec splitOp (d: Dictionary<char, MaybeDict>) i =
                                if i = cache.Length |> not && d.ContainsKey(cache[i]) then
                                    match d[cache[i]] with
                                    | ADict d' -> splitOp d' (i+1)
                                    | AValue v -> 
                                        tokens.Add v
                                        splitOp lp.operators (i+1)
                                else if d.ContainsKey('\000') then
                                    match d['\000'] with AValue t -> tokens.Add t | _ -> failwith "Impossible"
                                    splitOp lp.operators i
                                else if i = cache.Length then ()
                                else tokens.Add (TokenError $"Unknown symbol {cache[if i = cache.Length then i-1 else i]}")
                            splitOp lp.operators 0
                            innerRec tokens cursor
                    | c when isNumber c ->
                        let (cursor, isFloat) = scanNumber lp cursor source cache false
                        let s = cache.ToString()
                        tokens.Add(TokenFloat (Double.Parse s))
                        innerRec tokens cursor
                    | '$' ->
                        let cursor = scanIdentifier lp (cursor+1) source cache
                        let s = cache.ToString()
                        if s = "" || macros.ContainsKey s |> not then tokens.Add (TokenError $"Macro {s} is not defined")
                        else 
                            scan_  tokens lp macros[s] macros |> ignore
                        innerRec tokens cursor
                    | _ -> 
                        let cursor = scanIdentifier lp cursor source cache
                        let s = cache.ToString()
                        match s with
                        | s when lp.keywords.ContainsKey s -> tokens.Add(lp.keywords[s])
                        | _ -> tokens.Add(TokenIdentifier s)
                        innerRec tokens cursor
                innerRec tokens 0
            let rec private scanIdentifier lp cursor source cache =
                let current = source[cursor]
                if current = ' ' || current = '\"' || Array.contains current lp.newLine || Array.contains current lp.operatorChars then
                    cursor
                else 
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then cursor+1 else scanIdentifier lp (cursor+1) source cache
            let rec private scanNumber lp cursor source cache isFloat =
                let current = source[cursor]
                if isNumber current then
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then (cursor+1, isFloat) else scanNumber lp (cursor+1) source cache isFloat
                else if current = '.'  then 
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then (cursor+1, true) else scanNumber lp (cursor+1) source cache true
                else if current = 'e' && source.Length - cursor >= 2 && (isNumber source[cursor+1] || source[cursor+1] = '-') then 
                    cache.Append current |> ignore
                    scanNumber lp (cursor+1) source cache isFloat
                else (cursor, isFloat)
            let rec private scanSymbol lp cursor source cache =
                let current = source[cursor]
                if Array.contains current lp.operatorChars then
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then cursor+1 else scanSymbol lp (cursor+1) source cache
                else cursor
            let rec private scanString lp cursor source cache =
                let current = source[cursor]
                if current = '\"' then (cursor+1, false)
                else if source.Length-cursor >= 2 && current = '\\' then
                    let next = source[cursor+1]
                    (match next with
                    | 'n' -> cache.Append '\n'
                    | 'r' -> cache.Append '\r'
                    | '\\' -> cache.Append '\\'
                    | '\"' -> cache.Append '\"'
                    | _ -> cache.Append '?') |> ignore
                    if cursor+1 = source.Length-1 then (cursor+1, true) else scanString lp (cursor+2) source cache
                else
                    cache.Append current |> ignore
                    if cursor = source.Length-1 then (cursor, true) else scanString lp (cursor+1) source cache
            let rec private scanComment lp cursor source cache =
                if source[cursor] = '*' && source[cursor+1] = ')' then (cursor+2, false)
                else if cursor < source.Length-1 then scanComment lp (cursor+1) source cache
                else (cursor, true)
            let private isNumber c = c <= '9' && c >= '0'
            let makeLanguagePack (keywords: _stDict) (operators: _stDict) (newLine: char array) =
                let rec makeOpTree (operators: _stDict) =
                    let dictdict = Dictionary<char, MaybeDict>()
                    let rec addToDict (m: ReadOnlyMemory<char>) (t: Token) (d: Dictionary<char, MaybeDict>) =
                        let s = m.Span
                        if s.Length = 1 then
                            if d.ContainsKey(s[0]) then
                                match d[s[0]] with ADict d -> d.Add('\000', AValue t) | _ -> failwith "Impossible"
                            else d.Add(s[0], AValue t)
                        else
                            if d.ContainsKey(s[0]) then
                                match d[s[0]] with
                                | AValue v ->
                                    let d' = Dictionary<char, MaybeDict>()
                                    d'.Add('\000', AValue v)
                                    d[s[0]] <- ADict (Dictionary<char, MaybeDict>(d'))
                                | _ -> ()
                            else d.Add(s[0], ADict (Dictionary<char, MaybeDict>()))
                            match d[s[0]] with ADict d -> addToDict (m.Slice 1) t d | _ -> failwith "Impossible"
                    let rec makeDict (dictdict: Dictionary<char, MaybeDict>) (p: KeyValuePair<string, Token>) =
                        addToDict (p.Key.AsMemory()) p.Value dictdict
                    Seq.iter (makeDict dictdict) operators
                    dictdict
                let opChars = ResizeArray<char>()
                Seq.iter 
                <| (fun s -> Seq.iter (fun c -> if opChars.Contains c |> not then opChars.Add c) s)
                <| operators.Keys
                ()
                {keywords = keywords; operatorChars = opChars.ToArray(); operators = makeOpTree operators; newLine = newLine}
        module Parser =
            open Lexer
            let inline private eatSpace tokens =
                match tokens with
                | TokenSpace::r -> r
                | _ -> tokens
            let inline private eatSpaceAndNewLine tokens =
                match tokens with
                | TokenSpace::r -> r
                | TokenNewLine::r -> r
                | _ -> tokens
            let inline private eatSpaceOfTuple (t, tokens, errors) =
                match tokens with
                | TokenSpace::r -> t, r, errors
                | _ -> t, tokens, errors
            let parseAbstractTuple seperator multiLine makeTuple spSingleItem parseItem failMsg failValue tokens errors = 
                let rec innerRec tokens errors result =
                    match eatSpace tokens with
                    | TokenNewLine::r when multiLine ->
                        match r with
                        | TokenRightCurly::_ | TokenRightSquare::_ | TokenRightRound::_ -> 
                            result |> List.rev, tokens, errors
                        | _ ->
                            let item, tokens, errors = parseItem (eatSpaceAndNewLine r) errors
                            innerRec tokens errors (item::result)
                    | x::r when x = seperator ->
                        let item, tokens, errors = parseItem r errors
                        innerRec tokens errors (item::result)
                    | _ -> result |> List.rev, tokens, errors
                match parseItem ((if multiLine then eatSpaceAndNewLine else eatSpace) tokens) errors with
                | item, tokens, [] ->
                    let items, tokens, errors = innerRec tokens errors [item]
                    match errors with 
                    | [] -> (match items with | [e] when spSingleItem -> e | _ -> makeTuple items), tokens, errors
                    | _ -> failValue, tokens, $"{failMsg} item, but found {tokens[0..2]}"::errors
                | _, _, errors -> failValue, tokens, $"{failMsg} tuple, but found {tokens[0..2]}"::errors
            /// tuple = tupleItem {"," tupleItem}
            let parse tokens errors = 
                parseAbstractTuple TokenComma false Tuple true parseTupleItem "Expected expression" (Atom Unit) tokens errors
            let rec parseTupleItem (tokens: Token list) (errors: string list) =
                let l, tokens, errors = parseExpr tokens errors
                match eatSpace tokens with
                | TokenPipe::r
                | TokenRightPipe::r ->
                    let token, tokens, errors = parseTupleItem r errors
                    match token with
                    | Call args ->
                        Call (args@[l]), tokens, errors
                    | _ -> failwith "Impossible"
                | _ -> l, tokens, errors
            let rec parseExpr (tokens: Token list) (errors: string list) =
                let rec go tokens acc errors =
                    match tokens with
                    | TokenSpace::TokenPipe::_
                    | TokenSpace::TokenRightPipe::_
                    | TokenSpace::TokenRightRound::_ -> acc |> List.rev, tokens, errors
                    | TokenSpace::TokenLeftRound::TokenRightRound::xs 
                    | TokenSpace::TokenLeftRound::TokenSpace::TokenRightRound::xs ->
                        go xs ((Atom Unit)::acc) errors
                    | TokenSpace::TokenLeftRound::xs
                    | TokenLeftRound::xs ->
                        match parse xs errors |> eatSpaceOfTuple with
                        | ast, TokenRightRound::rest, errors -> go rest (ast::acc) errors
                        | _, x::_, _ -> [], tokens, $"Expected \")\", but found \"{x}\""::errors
                        | _ -> [], tokens, "Unexpected end of input"::errors
                    | TokenSpace::xs ->
                        let ast, rest, errors = parseAtom xs errors
                        go rest ((Atom ast)::acc) errors
                    | _ -> acc |> List.rev, tokens, errors
                match tokens |> eatSpace with
                | TokenLeftRound::xs ->
                    match parse xs errors |> eatSpaceOfTuple with
                    | ast, TokenRightRound::rest, errors -> 
                        let func = ast
                        let args, tokens, errors = go rest [] errors
                        Call (func::args), tokens, errors
                    | _, x::_, _ -> Atom Unit, tokens, $"Expected \")\", but found \"{x}\""::errors
                    | _ -> Atom Unit, tokens, "Unexpected end of input"::errors
                | _ ->
                    let func, rest, errors = parseAtom tokens errors
                    let args, tokens, errors = go rest [] errors
                    Call (Atom func::args), tokens, errors
                //go tokens [] errors
            let rec parseAtom tokens errors =
                match tokens |> eatSpace with
                | TokenFloat x::xs -> Number x, xs, errors
                | TokenString x::xs -> String x, xs, errors
                | TokenIdentifier x::xs -> Identifier x, xs, errors
                | x -> Unit, x, $"Unexpected token \"{x}\""::errors
                    // let getCommands filter =
                    //     let ret = Dictionary<string, ICommand>()
                    //     (fun (t: Type) ->
                    //         t.GetCustomAttributes(typeof<AestasCommandAttribute>, false)
                    //         |> Array.iter (fun attr ->
                    //             if attr :?> AestasCommandAttribute |> filter then
                    //                 let command = Activator.CreateInstance(t) :?> ICommand
                    //                 let name = (attr :?> AestasCommandAttribute).Name
                    //                 ret.Add(name, command)
                    //         )
                    //     )|> Array.iter <| Assembly.GetExecutingAssembly().GetTypes()
                    //     ret
        let keywords = readOnlyDict [
            "print", Lexer.TokenPrint
        ]
        let symbols = readOnlyDict [
            "<|", Lexer.TokenLeftPipe
            "|>", Lexer.TokenRightPipe
            "<-", Lexer.TokenLeftArrow
            "->", Lexer.TokenRightArrow
            ":", Lexer.TokenColon
            ";", Lexer.TokenSemicolon
            "(", Lexer.TokenLeftRound
            ")", Lexer.TokenRightRound
            "[", Lexer.TokenLeftSquare
            "]", Lexer.TokenRightSquare
            "{", Lexer.TokenLeftCurly
            "}", Lexer.TokenRightCurly
            ".", Lexer.TokenDot
            ",", Lexer.TokenComma
            "|", Lexer.TokenPipe
        ]
        let newLine = [|'\n';'\r';'`'|]
        let rec private executeAst (executer: ObsoletedCommandExeuter) (env: CommandEnvironment) (ast: Ast) =
            match ast with
            | Tuple items ->
                let rec go acc = function
                | Call h::t ->
                    go (executeAst executer env (Call h)::acc) t
                | Tuple h::t ->
                    go (executeAst executer env (Tuple h)::acc) t
                | Atom h::t ->
                    go (h::acc) t
                | [] -> acc |> List.rev |> AtomTuple
                go [] items
            | Call args ->
                let func = args.Head
                match executeAst executer env func with
                | Identifier "if" ->
                    match executeAst executer env args.Tail.Head, args.Tail.Tail with
                    | Number flag, t::f::[] ->
                        if flag = 0. then
                            executeAst executer env f
                        else
                            executeAst executer env t
                    | _, _ -> env.log "if condition trueBranch falseBranch"; Unit
                | Identifier name when executer.Commands.ContainsKey name ->
                    match env.privilege with
                    | x when x < executer.Commands[name].Privilege -> 
                        env.log $"Permission denied"; Unit
                    | _ ->
                        let args = List.map (fun x -> executeAst executer env x) args.Tail
                        executer.Commands[name].Execute env args
                | Identifier name ->
                    env.log $"Command not found: {name}"
                    Unit
                | x -> 
                    env.log $"Expected identifier, but found {x}"
                    Unit
            | Atom x -> x
        let languagePack = Lexer.makeLanguagePack keywords symbols newLine
        let execute (executer: ObsoletedCommandExeuter) (env: CommandEnvironment) (cmd: string) =
            try
            let tokens = Lexer.scanWithoutMacro languagePack cmd
            let ast, _, errors = Parser.parse tokens []
            Logger.logInfo[0] <| sprintf "%A, %A, %A" tokens ast errors
            match errors with
            | [] -> 
                match executeAst executer env ast with
                | Unit -> ()
                | x -> env.log <| x.ToString()
            | _ -> env.log <| String.Join("\n", "Error occured:"::errors)
            with ex -> env.log <| ex.ToString()
    module CommandHelper =
        /// Use this type to tell the parser how to parse the arguments
        type CommandParameters =
            /// To require a unit value parameter, Ctor: name
            | ParamUnit of Name: string
            /// To require a string value parameter, Ctor: name * default value
            | ParamString of Name: string*(string option)
            /// To require a identifier value parameter, Ctor: name * default value
            | ParamIdentifier of Name: string*(string option)
            /// To require a number value parameter, Ctor: name * default value
            | ParamNumber of Name: string*(float option)
            /// To require a tuple value parameter, Ctor: name * default value
            | ParamTuple of Name: string*(Atom list option)
            /// To require a object value parameter, Ctor: name * default value
            | ParamObject of Name: string*(Map<string, Atom> option)
        /// Parse result
        type CommandArguments =
            /// Indicates that the argument is not provided
            | ArgNone
            /// Indicates that the argument is provided a unit value
            | ArgUnit
            /// Indicates that the argument is provided a string value
            | ArgString of string
            /// Indicates that the argument is provided a identifier value
            | ArgIdentifier of Name: string
            /// Indicates that the argument is provided a number value
            | ArgNumber of Name: float
            /// Indicates that the argument is provided a tuple value
            | ArgTuple of Name: Atom list
            /// Indicates that the argument is provided a object value
            | ArgObject of Name: Map<string, Atom>
        let inline getParamName p =
            match p with 
            | ParamUnit n -> n
            | ParamString (n, _) -> n
            | ParamIdentifier (n, _) -> n
            | ParamNumber (n, _) -> n
            | ParamTuple (n, _) -> n
            | ParamObject (n, _) -> n
        let inline getParamDefaultValue p =
            match p with 
            | ParamUnit _ -> ArgNone
            | ParamString (_, d) -> match d with | Some x -> ArgString x | None -> ArgNone
            | ParamIdentifier (_, d) -> match d with | Some x -> ArgIdentifier x | None -> ArgNone
            | ParamNumber (_, d) -> match d with | Some x -> ArgNumber x | None -> ArgNone
            | ParamTuple (_, d) -> match d with | Some x -> ArgTuple x | None -> ArgNone
            | ParamObject (_, d) -> match d with | Some x -> ArgObject x | None -> ArgNone
        let parseArguments (params': CommandParameters seq) args =
            let params' = params' |> Seq.map (fun p -> getParamName p, p) |> Map.ofSeq
            let rec go params' args acc errors =
                match args with
                | [] -> params', acc, errors
                | Identifier x::v::t when params' |> Map.containsKey x ->
                    match v, params'[x] with
                    | String s, ParamString (_, _) -> go (Map.remove x params') t (Map.add x (ArgString s) acc) errors
                    | Identifier i, ParamIdentifier (_, _) -> go (Map.remove x params') t (Map.add x (ArgIdentifier i) acc) errors
                    | Number n, ParamNumber (_, _) -> go (Map.remove x params') t (Map.add x (ArgNumber n) acc) errors
                    | AtomTuple t', ParamTuple (_, _) -> go (Map.remove x params') t (Map.add x (ArgTuple t') acc) errors
                    | AtomObject o, ParamObject (_, _) -> go (Map.remove x params') t (Map.add x (ArgObject o) acc) errors
                    | Unit, ParamUnit _ -> go (Map.remove x params') t (Map.add x ArgUnit acc) errors
                    | _, ParamUnit _ -> go (Map.remove x params') t (Map.add x ArgUnit acc) errors
                    | _, _ -> go (Map.remove x params') t (Map.add x ArgUnit acc) ($"Couldn't parse argument {x}: Type mismatch"::errors)
                | Identifier x::[] when params' |> Map.containsKey x ->
                    match params'[x] with
                    | ParamUnit _ -> go (Map.remove x params') [] (Map.add x ArgUnit acc) errors
                    | _ -> go (Map.remove x params') [] (Map.add x ArgUnit acc) ($"Couldn't parse argument {x}: Unexpected end of input"::errors)
                | _ -> 
                    params', acc, $"""Couldn't parse argument {Map.keys params' |> Seq.map (fun x -> x.ToString()) |> String.concat ", "}: Bad input, ignored {args}"""::errors
            let params', args, errors = go params' args Map.empty []
            if params'.Count = 0 then args, errors else
            Map.fold (fun acc k v -> Map.add k (getParamDefaultValue v) acc) args params', errors