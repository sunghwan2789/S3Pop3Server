using MediatR;

namespace S3Pop3Server.Queries
{
    public class GetMailboxContentQuery : IRequest<GetMailboxContentQueryResponse>
    {
        public Email Item { get; set; }
    }
}
