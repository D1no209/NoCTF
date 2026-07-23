namespace NoCTF.Application.Messaging;

public interface ITransactionalMessageOutbox
{
    ValueTask PublishAsync<T>(T message);
    Task FlushOutgoingMessagesAsync();
}
