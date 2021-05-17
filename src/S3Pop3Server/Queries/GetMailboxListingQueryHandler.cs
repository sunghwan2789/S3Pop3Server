using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using MediatR;
using Microsoft.Extensions.Options;

namespace S3Pop3Server.Queries
{
    public class GetMailboxListingQueryHandler : IRequestHandler<GetMailboxListingQuery, GetMailboxListingQueryResponse>
    {
        private readonly IAmazonS3 _s3;
        private readonly S3Options _options;

        public GetMailboxListingQueryHandler(IAmazonS3 s3, IOptions<S3Options> options)
        {
            _s3 = s3;
            _options = options.Value;
        }

        public async Task<GetMailboxListingQueryResponse> Handle(GetMailboxListingQuery request, CancellationToken cancellationToken)
        {
            var response = await _s3.ListObjectsAsync(_options.BucketName, cancellationToken);

            return new GetMailboxListingQueryResponse
            {
                Items = response.S3Objects.Select(obj => new Email
                {
                    Id = obj.Key,
                    Size = obj.Size,
                }).ToList(),
            };
        }
    }
}
