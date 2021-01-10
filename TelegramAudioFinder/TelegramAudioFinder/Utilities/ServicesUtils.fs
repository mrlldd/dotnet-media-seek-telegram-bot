module TelegramAudioFinder.Services.Utils

open System
open System.Threading
open FSharp.Data.Runtime.Caching
open Telegram.Bot.Types
open TelegramAudioFinder
open Microsoft.FSharp.Reflection

type Service =
    | YouTube
    | SoundCloud
    | All

[<Literal>]
let pagePerService = 5

let servicesCount = FSharpType.GetUnionCases(typeof<Service>).Length - 1 // excluding All union case
let pageLength = servicesCount * pagePerService

let defaultCacheTimeout = TimeSpan.FromMinutes(1 |> float)

let private keyFactory (pair: string * int) =
    let (q, o) = pair
    sprintf "%s;%i" q o

let createServiceInMemoryCache cacheTimeout =
    let cache = createInMemoryCache cacheTimeout

    { new ICache<(string * int), _> with
        member __.Set(key, value) = cache.Set(keyFactory key, value)

        member x.TryRetrieve(key, ?extendCacheExpiration) =
            cache.TryRetrieve(keyFactory key, ?extendCacheExpiration = extendCacheExpiration)

        member __.Remove(key) = cache.Remove(keyFactory key) }

type ServiceSearchArgument = int * InlineQuery * CancellationToken

type ServiceSearchResult = Result<seq<UnifiedSearchResult>, exn>
