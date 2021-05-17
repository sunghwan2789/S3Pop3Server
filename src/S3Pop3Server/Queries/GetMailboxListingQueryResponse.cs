using System.Collections.Generic;

namespace S3Pop3Server.Queries
{
    public class GetMailboxListingQueryResponse
    {
        public IEnumerable<Email> Items { get; init; }
    }
}
