module TelegramAudioFinder.Services.Utils

open System

[<Literal>]
let pageLength = 10
let cacheTimeout = TimeSpan.FromMinutes(1 |> float)

