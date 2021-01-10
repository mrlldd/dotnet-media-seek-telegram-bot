namespace TelegramAudioFinder

type UnifiedSearchResult = {
    ThumbUrl: string option
    Title: string 
    Description: string option
    Url: string
    PlayCount: uint64
}