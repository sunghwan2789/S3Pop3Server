using MediatR;

namespace S3Pop3Server.Queries
{
    public class GetMailboxContentQuery : IRequest<GetMailboxContentQueryResponse>
    {
        public string MailboxName { get; set; }
        public Email Item { get; set; }
    }
}
