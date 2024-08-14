open System
open System.IO
open System.Collections.Generic
let ifExistThenGetFiles path = 
    if Directory.Exists path then Directory.GetFiles(path) else [||]
let ( @@ ) (a: 't[]) (b: 't[]) = Array.concat [a; b]
let checkDir dir = if Directory.Exists dir then () else Directory.CreateDirectory dir |> ignore
checkDir "src"
checkDir "src/adapters"
checkDir "src/bots"
checkDir "src/commands"
checkDir "src/llms"
checkDir "src/plugins"
let adapters = ifExistThenGetFiles "src/adapters/"
let bots = ifExistThenGetFiles "src/bots/"
let commands = ifExistThenGetFiles "src/commands/"
let llms = ifExistThenGetFiles "src/llms/"
let plugins = ifExistThenGetFiles "src/plugins/"
let all = adapters @@ bots @@ commands @@ llms @@ plugins
let proj = ResizeArray<string>()
let nuget = ResizeArray<string*string>()
let foldIList (folder: 'state -> 't -> 'state) (state: 'state) (list: IList<'t>) =
    let rec go i state =
        if i >= list.Count then state
        else
            go (i+1) (folder state list[i])
    go 0 state
let src x = $"src/{x}"
let parseRef (r: string) =
    let span = r.AsSpan().Slice 4
    if span.StartsWith "csproj" || span.StartsWith "fsproj" then
        let path = span.Slice 7
        proj.Add(path.ToString())
    elif span.StartsWith "nuget" then
        let path = span.Slice 6
        let spiltIndex = path.IndexOf '='
        nuget.Add(path.Slice(0, spiltIndex).ToString(), path.Slice(spiltIndex+1).ToString())
    else ()
let findRef (reader: StreamReader) = 
    let rec go() =
        let line = reader.ReadLine()
        if line.StartsWith "//!" then
            printfn "Finded ref: %s" line
            parseRef line
            go()
        else ()
    go()
all |> Array.iter (
    fun x -> 
        printfn "Finded file: %s" x
        use reader = new StreamReader(x)
        findRef reader
        )
printfn "Project references: %A" proj
printfn "PackageReference: %A" nuget
let spaceLine = ""
let projectStart = """<Project Sdk="Microsoft.NET.Sdk">"""
let projectEnd = """</Project>"""
let propertyGroup = """  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <InvariantGlobalization>true</InvariantGlobalization>
    <LangVersion>preview</LangVersion>
    <PlatformTarget>AnyCPU</PlatformTarget>
    <ServerGarbageCollection>true</ServerGarbageCollection>
    <NoWarn>3535, 3536</NoWarn>
  </PropertyGroup>"""
let itemGroupStart = """  <ItemGroup>"""
let itemGroupEnd = """  </ItemGroup>"""
let compileInclude x = $"""    <Compile Include="{x}" />"""
let packageReference n v = $"""    <PackageReference Include="{n}" Version="{v}" />"""
let projectReference p = $"""    <ProjectReference Include="{p}" />"""
let fsharpCore = """  <ItemGroup>
    <PackageReference Update="FSharp.Core" Version="8.0.300" />
  </ItemGroup>"""
let xml = [
    [projectStart]
    [spaceLine]
    [propertyGroup]
    [spaceLine]
    [itemGroupStart]
    [
        "prim.fs" |> src |> compileInclude
        "core.fs" |> src |> compileInclude
        "auto-init.fs" |> src |> compileInclude
    ]
    llms |> foldIList (fun list x -> compileInclude x::list) []
    adapters |> foldIList (fun list x -> compileInclude x::list) []
    plugins |> foldIList (fun list x -> compileInclude x::list) []
    commands |> foldIList (fun list x -> compileInclude x::list) []
    bots |> foldIList (fun list x -> compileInclude x::list) []
    ["cli.fs" |> src |> compileInclude]
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    [packageReference "FSharp.SystemTextJson" "1.3.13"]
    nuget |> foldIList (fun list (n, v) -> packageReference n v::list) []
    [itemGroupEnd]
    [spaceLine]
    [itemGroupStart]
    proj |> foldIList (fun list x -> projectReference x::list) []
    [itemGroupEnd]
    [spaceLine]
    [fsharpCore]
    [spaceLine]
    [projectEnd]
]
let xmlString = xml |> List.collect id |> String.concat "\n"
let writer = new StreamWriter("aestas.fsproj")
writer.Write(xmlString)
writer.Close()
printfn "Generated aestas.fsproj"
