using System.Collections.Generic;
using MediatR;

namespace S3Pop3Server.Commands
{
    public class DeleteMailboxItemsCommand : IRequest<bool>
    {
        public IEnumerable<Email> Items { get; set; }
    }
}
