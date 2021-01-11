module MediaSeekBot.Utils

open System
open System.Collections.Generic
open System.IO
open System.Threading
open FsConfig
open Microsoft.Extensions.Configuration
open Newtonsoft.Json
open Newtonsoft.Json.Serialization

let cts = new CancellationTokenSource()

let json (obj, (format: Formatting)) = JsonConvert.SerializeObject(obj, format)

let private snakeCaseSerializerSettings =
    let resolver = DefaultContractResolver()
    resolver.NamingStrategy <- SnakeCaseNamingStrategy()
    let settings = JsonSerializerSettings()
    settings.ContractResolver <- resolver
    settings

let fromJson<'a> s =
    JsonConvert.DeserializeObject<'a>(s, snakeCaseSerializerSettings)

let cacheInMemory func =
    let dictionary = Dictionary<_, _>()

    fun key ->
        match dictionary.TryGetValue key with
        | (true, value) -> value
        | _ ->
            let calculated = func key
            dictionary.Add(key, calculated)
            calculated

let getEnvironmentVariable =
    cacheInMemory Environment.GetEnvironmentVariable

let private rawConfig =
    ConfigurationBuilder()
        .SetBasePath(Directory.GetCurrentDirectory())
        .AddJsonFile("appsettings.json", false, true)
        .AddJsonFile(sprintf "appsettings.%s.json" (getEnvironmentVariable "environment"), true)
        .AddUserSecrets<ApplicationConfig>()
        .AddEnvironmentVariables()
        .Build()

let applicationConfig =
    match AppConfig(rawConfig).Get<ApplicationConfig>() with
    | Ok cfg -> cfg
    | Error error ->
        match error with
        | NotFound envVarName -> failwithf "Environment variable %s not found" envVarName
        | BadValue (envVarName, value) -> failwithf "Environment variable %s - %s has invalid value" envVarName value
        | NotSupported msg -> failwith msg

let bindAsync (af: _ -> Async<_>) (r: Result<_, _>) =
    async {
        match r with
        | Ok ok -> return! af (ok)
        | Error e -> return Error e
    }