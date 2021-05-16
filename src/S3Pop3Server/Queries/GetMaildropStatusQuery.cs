using MediatR;

namespace S3Pop3Server.Queries
{
    public class GetMaildropStatusQuery : IRequest<GetMaildropStatusQueryResponse>
    {
    }
}
