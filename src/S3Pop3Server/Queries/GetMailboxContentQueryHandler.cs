using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using MediatR;
using Microsoft.Extensions.Options;

namespace S3Pop3Server.Queries
{
    public class GetMailboxContentQueryHandler : IRequestHandler<GetMailboxContentQuery, GetMailboxContentQueryResponse>
    {
        private readonly IAmazonS3 _s3;
        private readonly S3Options _options;

        public GetMailboxContentQueryHandler(IAmazonS3 s3, IOptions<S3Options> options)
        {
            _s3 = s3;
            _options = options.Value;
        }

        public async Task<GetMailboxContentQueryResponse> Handle(GetMailboxContentQuery request, CancellationToken cancellationToken)
        {
            var response = await _s3.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _options.BucketName,
                Key = request.Item.Id,
            });

            return new GetMailboxContentQueryResponse
            {
                Item = request.Item,
                ContentStream = response.ResponseStream,
            };
        }
    }
}
