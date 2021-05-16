using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using MediatR;
using Microsoft.Extensions.Options;

namespace S3Pop3Server.Queries
{
    public class GetMaildropStatusQueryHandler : IRequestHandler<GetMaildropStatusQuery, GetMaildropStatusQueryResponse>
    {
        private readonly IAmazonS3 _s3;
        private readonly S3Options _options;

        public GetMaildropStatusQueryHandler(IAmazonS3 s3, IOptions<S3Options> options)
        {
            _s3 = s3;
            _options = options.Value;
        }

        public async Task<GetMaildropStatusQueryResponse> Handle(GetMaildropStatusQuery request, CancellationToken cancellationToken)
        {
            var response = await _s3.ListObjectsAsync(_options.BucketName, cancellationToken);

            return new GetMaildropStatusQueryResponse
            {
                MessageCount = response.S3Objects.Count,
                Size = response.S3Objects.Sum(file => file.Size),
            };
        }
    }
}
