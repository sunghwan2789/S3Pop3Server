using System.Collections.Generic;
using MediatR;

namespace S3Pop3Server.Commands
{
    public class DeleteMailboxItemsCommand : IRequest<bool>
    {
        public string MailboxName { get; set; }
        public IEnumerable<Email> Items { get; set; }
    }
}
