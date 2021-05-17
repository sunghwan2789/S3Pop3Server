using System.IO;

namespace S3Pop3Server.Queries
{
    public class GetMailboxContentQueryResponse
    {
        public Email Item { get; set; }
        public Stream ContentStream { get; set; }
    }
}
