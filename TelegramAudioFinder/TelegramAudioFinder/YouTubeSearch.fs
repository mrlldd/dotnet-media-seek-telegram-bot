module TelegramAudioFinder.YouTubeSearch

open Google.Apis.Services
open Google.Apis.Util
open Google.Apis.YouTube.v3
open Google.Apis.YouTube.v3.Data
open Telegram.Bot.Types
open TelegramAudioFinder.Utils
open TelegramAudioFinder.Services.Utils
open FSharp.Data.Runtime.Caching

let private youtubeService =
    let youTubeConfig = applicationConfig.Services.Youtube
    let init = BaseClientService.Initializer()
    init.ApiKey <- youTubeConfig.Key
    init.ApplicationName <- youTubeConfig.AppName
    new YouTubeService(init)

let private youtubeItemsMapper (item: SearchResult) =
    let snippet = item.Snippet

    { Url = sprintf "https://youtube.com/watch?v=%s" item.Id.VideoId
      Title = snippet.Title
      Description = snippet.Description
      ThumbUrl = snippet.Thumbnails.Default__.Url }

let private youtubeContinuationTokensCache = createInMemoryCache cacheTimeout

let youTubeSearch (offset, inlineQuery: InlineQuery, token) =
    async {
        let list =
            [ "snippet" ]
            |> Repeatable
            |> youtubeService.Search.List

        let keyFactory = sprintf "%s;%i" inlineQuery.Query

        match offset
              |> keyFactory
              |> youtubeContinuationTokensCache.TryRetrieve with
        | Some res -> list.PageToken <- res
        | _ -> ()

        list.Q <- inlineQuery.Query
        list.Type <- [ "video" ] |> Repeatable
        list.MaxResults <- pageLength |> int64
        list.RelevanceLanguage <- inlineQuery.From.LanguageCode

        let! result =
            list.ExecuteAsync(token)
            |> Async.AwaitTask
            |> Async.Catch

        let nextPageOffset = offset + pageLength

        return
            match result with
            | Choice1Of2 success ->
                youtubeContinuationTokensCache.Set(keyFactory nextPageOffset, success.NextPageToken)

                Ok
                    (success.Items
                     |> Seq.toList
                     |> Seq.map youtubeItemsMapper)
            | Choice2Of2 error -> Error error
    }
