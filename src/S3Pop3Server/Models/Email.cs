namespace S3Pop3Server
{
    public record Email
    {
        public int MessageNumber { get; init; }
        public string Id { get; init; }
        public long Size { get; init; }
    }
}
