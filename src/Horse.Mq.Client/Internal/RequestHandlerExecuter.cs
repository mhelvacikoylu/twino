using System;
using System.Linq;
using System.Threading.Tasks;
using Horse.Protocols.Hmq;

namespace Horse.Mq.Client.Internal
{
    internal class RequestHandlerExecuter<TRequest, TResponse> : ConsumerExecuter
    {
        private readonly Type _handlerType;
        private readonly IHorseRequestHandler<TRequest, TResponse> _handler;
        private readonly Func<IConsumerFactory> _handlerFactoryCreator;

        public RequestHandlerExecuter(Type handlerType, IHorseRequestHandler<TRequest, TResponse> handler, Func<IConsumerFactory> handlerFactoryCreator)
        {
            _handlerType = handlerType;
            _handler = handler;
            _handlerFactoryCreator = handlerFactoryCreator;
        }

        public override void Resolve(ModelTypeConfigurator defaultOptions = null)
        {
            base.Resolve(defaultOptions);
            ResolveAttributes(_handlerType);
        }

        public override async Task Execute(HorseClient client, HorseMessage message, object model)
        {
            Exception exception = null;
            IConsumerFactory consumerFactory = null;
            bool respond = false;

            try
            {
                TRequest requestModel = (TRequest) model;
                IHorseRequestHandler<TRequest, TResponse> handler;

                if (_handler != null)
                    handler = _handler;
                else if (_handlerFactoryCreator != null)
                {
                    consumerFactory = _handlerFactoryCreator();
                    consumerFactory.CreateScope();
                    object consumerObject = await consumerFactory.CreateConsumer(_handlerType);
                    handler = (IHorseRequestHandler<TRequest, TResponse>) consumerObject;
                }
                else
                    throw new ArgumentNullException("There is no consumer defined");

                try
                {
                    await RunBeforeInterceptors(message, client, consumerFactory);
                    TResponse responseModel = await Handle(handler, requestModel, message, client);
                    HorseResultCode code = responseModel is null ? HorseResultCode.NoContent : HorseResultCode.Ok;
                    HorseMessage responseMessage = message.CreateResponse(code);

                    if (responseModel != null)
                        responseMessage.Serialize(responseModel, client.JsonSerializer);

                    respond = true;
                    await client.SendAsync(responseMessage);
                    await RunAfterInterceptors(message, client, consumerFactory);
                }
                catch (Exception e)
                {
                    ErrorResponse errorModel = await handler.OnError(e, requestModel, message, client);

                    if (errorModel.ResultCode == HorseResultCode.Ok)
                        errorModel.ResultCode = HorseResultCode.Failed;

                    HorseMessage responseMessage = message.CreateResponse(errorModel.ResultCode);

                    if (!string.IsNullOrEmpty(errorModel.Reason))
                        responseMessage.SetStringContent(errorModel.Reason);

                    respond = true;
                    await client.SendAsync(responseMessage);
                    throw;
                }
            }
            catch (Exception e)
            {
                if (!respond)
                {
                    try
                    {
                        HorseMessage response = message.CreateResponse(HorseResultCode.InternalServerError);
                        await client.SendAsync(response);
                    }
                    catch
                    {
                    }
                }

                await SendExceptions(message, client, e);
                exception = e;
            }
            finally
            {
                if (consumerFactory != null)
                    consumerFactory.Consumed(exception);
            }
        }

        private async Task<TResponse> Handle(IHorseRequestHandler<TRequest, TResponse> handler, TRequest request, HorseMessage message, HorseClient client)
        {
            if (Retry == null)
                return await handler.Handle(request, message, client);

            int count = Retry.Count == 0 ? 100 : Retry.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    return await handler.Handle(request, message, client);
                }
                catch (Exception e)
                {
                    Type type = e.GetType();
                    if (Retry.IgnoreExceptions != null && Retry.IgnoreExceptions.Length > 0)
                    {
                        if (Retry.IgnoreExceptions.Any(x => x.IsAssignableFrom(type)))
                            throw;
                    }

                    if (Retry.DelayBetweenRetries > 0)
                        await Task.Delay(Retry.DelayBetweenRetries);
                }
            }

            throw new OperationCanceledException("Reached to maximum retry count and execution could not be completed");
        }
    }
}