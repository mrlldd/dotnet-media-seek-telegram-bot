module TelegramAudioFinder.Search.YouTube

open Google.Apis.Services
open Google.Apis.Util
open Google.Apis.YouTube.v3
open Google.Apis.YouTube.v3.Data
open TelegramAudioFinder
open TelegramAudioFinder.Utils
open TelegramAudioFinder.Services.Utils

let private youtubeService =
    let youTubeConfig = applicationConfig.Services.YouTube
    let init = BaseClientService.Initializer()
    init.ApiKey <- youTubeConfig.Key
    init.ApplicationName <- youTubeConfig.AppName
    new YouTubeService(init)

let private youtubeItemsMapper (item: SearchResult, video: VideoStatistics) =
    let snippet = item.Snippet

    { Url = sprintf "https://youtube.com/watch?v=%s" item.Id.VideoId
      Title = snippet.Title
      Description = Some snippet.Description
      ThumbUrl = Some snippet.Thumbnails.Default__.Url
      PlayCount = if video.ViewCount.HasValue then video.ViewCount.Value else 0 |> uint64 }

let private youtubeContinuationTokensCache =
    createServiceInMemoryCache defaultCacheTimeout

let private responseBinder (success: SearchListResponse) =
    async {
        let list =
            [ "statistics" ]
            |> Repeatable
            |> youtubeService.Videos.List

        let dict =
            success.Items
            |> Seq.map (fun x -> (x.Id.VideoId, x))
            |> Map.ofSeq

        list.Id <- dict |> Seq.map (fun x -> x.Key) |> Repeatable

        let! resp =
            list.ExecuteAsync()
            |> Async.AwaitTask
            |> Async.Catch

        return
            resp
            |> Result.ofChoice
            |> Result.bind (fun ok ->
                ok.Items
                |> Seq.toList
                |> Seq.map (fun video -> youtubeItemsMapper (dict.Item(video.Id), video.Statistics))
                |> Ok)
    }

let youTubeSearch (tup: ServiceSearchArgument): Async<ServiceSearchResult> =
    let (offset, inlineQuery, token) = tup

    async {
        let list =
            [ "snippet" ]
            |> Repeatable
            |> youtubeService.Search.List

        match (inlineQuery.Query, offset)
              |> youtubeContinuationTokensCache.TryRetrieve with
        | Some res -> list.PageToken <- res
        | _ -> ()

        list.Q <- inlineQuery.Query
        list.Type <- [ "video" ] |> Repeatable
        list.MaxResults <- pagePerService |> int64
        list.RelevanceLanguage <- inlineQuery.From.LanguageCode

        let! snippets =
            list.ExecuteAsync(token)
            |> Async.AwaitTask
            |> Async.Catch

        return!
            snippets
            |> Result.ofChoice
            |> bindAsync (fun success ->
                youtubeContinuationTokensCache.Set((inlineQuery.Query, offset + pagePerService), success.NextPageToken)
                responseBinder success)
    }
