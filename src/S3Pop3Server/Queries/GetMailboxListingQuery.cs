using MediatR;

namespace S3Pop3Server.Queries
{
    public class GetMailboxListingQuery : IRequest<GetMailboxListingQueryResponse>
    {
    }
}
