using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon.S3;
using Amazon.S3.Model;
using MediatR;
using Microsoft.Extensions.Options;

namespace S3Pop3Server.Commands
{
    public class DeleteMailboxItemsCommandHandler : IRequestHandler<DeleteMailboxItemsCommand, bool>
    {
        private readonly IAmazonS3 _s3;

        public DeleteMailboxItemsCommandHandler(IAmazonS3 s3)
        {
            _s3 = s3;
        }

        public async Task<bool> Handle(DeleteMailboxItemsCommand request, CancellationToken cancellationToken)
        {
            var response = await _s3.DeleteObjectsAsync(new DeleteObjectsRequest
            {
                BucketName = request.MailboxName,
                Objects = request.Items
                    .Select(item => new KeyVersion
                    {
                        Key = item.Id
                    })
                    .ToList(),
            }, cancellationToken);

            return !response.DeleteErrors.Any();
        }
    }
}
