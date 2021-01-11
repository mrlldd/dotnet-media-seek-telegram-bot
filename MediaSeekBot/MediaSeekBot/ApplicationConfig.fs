namespace MediaSeekBot

type TelegramConfig = {
    Token: string
    WebhookUrl: string
    MaxConnections: int
    DeveloperChatId: int
}

type YouTubeConfig = {
    AppName: string
    Key: string
}

type SoundCloudConfig = {
    ClientId: string
}

type ServicesConfig = {
    YouTube: YouTubeConfig
    SoundCloud: SoundCloudConfig
}

type ApplicationConfig = {
    Telegram: TelegramConfig
    Services: ServicesConfig
}