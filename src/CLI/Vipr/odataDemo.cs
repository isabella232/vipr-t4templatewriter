﻿namespace Microsoft.CoreServices.Extensions

{

    using global::Microsoft.OData.Client;

    using global::Microsoft.OData.Core;



    using System;

    using System.Collections.Generic;

    using System.ComponentModel;

    using System.Linq;

    using System.Reflection;



    internal class LowerCasePropertyAttribute : System.Attribute

    {

    }



    public interface IEntityBase

    {

		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        global::System.Threading.Tasks.Task UpdateAsync(bool dontSave = false);

		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        global::System.Threading.Tasks.Task DeleteAsync(bool dontSave = false);

    }



    internal class RestShallowObjectFetcher  : BaseEntityType

    {

        private string _path;

        internal new DataServiceContextWrapper Context 

        { 

            get

            {

                return (DataServiceContextWrapper)base.Context;

            }

            private set

            {

                base.Context = value;

            }

        }



        internal RestShallowObjectFetcher () {}

        

        internal void Initialize(

            DataServiceContextWrapper context,

            string path)

        {

            Context = context;

            _path = path;

        }



        protected string GetPath(string propertyName)

        {

            return propertyName == null ? this._path : this._path + "/" + propertyName;

        }



        protected System.Uri GetUrl()

        {

            return new Uri(Context.BaseUri.ToString().TrimEnd('/') + "/" + GetPath(null));

        }

    }



    public partial class DataServiceContextWrapper : DataServiceContext

    {

        private object _syncLock = new object();

        private string _accessToken;

        private global::System.Func<global::System.Threading.Tasks.Task<string>> _accessTokenGetter;

        private System.Func<System.Threading.Tasks.Task> _accessTokenSetter;

        private HashSet<EntityBase> _modifiedEntities = new HashSet<EntityBase>();



        private readonly string XClientStringClientTag = string.Format("Office 365 API Tools {0}", "1.1.0612");



        public void UpdateObject(EntityBase entity)

        {

            if (GetEntityDescriptor(entity) != null)

            {

                _modifiedEntities.Add(entity);

                base.UpdateObject(entity);

            }

        }



        private async global::System.Threading.Tasks.Task SetToken()

        {

            var token = await _accessTokenGetter();

            lock(_syncLock)

            {

                _accessToken = token;

            }

        }



        public DataServiceContextWrapper(Uri serviceRoot, global::Microsoft.OData.Client.ODataProtocolVersion maxProtocolVersion, global::System.Func<global::System.Threading.Tasks.Task<string>> accessTokenGetter)

            : base(serviceRoot, maxProtocolVersion)

        {

            _accessTokenGetter = accessTokenGetter;

            _accessTokenSetter = SetToken;

            

            IgnoreMissingProperties = true;



            BuildingRequest += (sender, args) =>

            {

           

                args.Headers.Add("Authorization", "Bearer " + _accessToken);

                args.Headers.Add("X-ClientService-ClientTag", XClientStringClientTag);

            };

        

            Configurations.RequestPipeline.OnEntryStarting((args) =>

            {

                var entity = (EntityBase)args.Entity;



                if ((!entity.ChangedProperties.IsValueCreated || entity.ChangedProperties.Value.Count == 0))

                {

                    args.Entry.Properties = new ODataProperty[0];

                    return;

                }



                if (!_modifiedEntities.Contains(entity))

                {

                    _modifiedEntities.Add(entity);

                }



                IEnumerable<ODataProperty> properties = new ODataProperty[0];



                if (entity.ChangedProperties.IsValueCreated)

                {

                    properties = properties.Concat(args.Entry.Properties.Where(i => entity.ChangedProperties.Value.Contains(i.Name)));

                }



                args.Entry.Properties = properties;

            });



            Configurations.ResponsePipeline.OnEntityMaterialized((args) =>

            {

                var entity = (EntityBase)args.Entity;



                entity.ResetChanges();

            });



            OnCreated();

        }



        partial void OnCreated();



        internal System.Type DefaultResolveTypeInternal(string typeName, string fullNamespace, string languageDependentNamespace)

        {

            return DefaultResolveType(typeName, fullNamespace, languageDependentNamespace);

        }



        internal string DefaultResolveNameInternal(global::System.Type clientType, string fullNamespace, string languageDependentNamespace)

        {

            if (clientType.Namespace.Equals(languageDependentNamespace, global::System.StringComparison.Ordinal))

            {

                return string.Concat(fullNamespace, ".", clientType.Name);

            }



            return string.Empty;

        }



        public async System.Threading.Tasks.Task<TInterface> ExecuteSingleAsync<TSource, TInterface>(DataServiceQuery<TSource> inner)

        {

            try

            {

                await SetToken();

                return await global::System.Threading.Tasks.Task.Factory.FromAsync<TInterface>(

                    inner.BeginExecute,

                    new global::System.Func<global::System.IAsyncResult, TInterface>(i => 

                        global::System.Linq.Enumerable.SingleOrDefault(

                        global::System.Linq.Enumerable.Cast<TInterface>(inner.EndExecute(i)))),

                    global::System.Threading.Tasks.TaskCreationOptions.None);

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }



        public async System.Threading.Tasks.Task<IBatchElementResult[]> ExecuteBatchAsync(params IReadOnlyQueryableSetBase[] queries)

        {

            try

            {

                var requests = (from i in queries select (DataServiceRequest)i.Query).ToArray();



                await SetToken();



                var responses = await global::System.Threading.Tasks.Task.Factory.FromAsync<DataServiceRequest[], DataServiceResponse>(

                    (q, callback, state) => BeginExecuteBatch(callback, state, q), // need to reorder parameters

                    EndExecuteBatch,

                    requests,

                    null);



                var retVal = new IBatchElementResult[queries.Length];



                var index = 0;

                foreach (var response in responses)

                {

                    Type tConcrete = ((IConcreteTypeAccessor)queries[index]).ConcreteType;

                    Type tInterface = ((IConcreteTypeAccessor)queries[index]).ElementType;

                    

                    var pcType = typeof(PagedCollection<,>).MakeGenericType(tInterface, tConcrete);

                    var pcTypeInfo = pcType.GetTypeInfo();

                    var PCCreator = pcTypeInfo.GetDeclaredMethod("Create");



                    // Handle an error response. 

                    // from http://msdn.microsoft.com/en-us/library/dd744838(v=vs.110).aspx

                    if (response.StatusCode > 299 || response.StatusCode < 200)

                    {

                        retVal[index] = new BatchElementResult(ProcessException(response.Error) ?? response.Error);

                    }

                    else

                    {

                        retVal[index] = new BatchElementResult((IPagedCollection)PCCreator.Invoke(null, new object[] { this, response }));

                    }



                    index++;

                }



                return retVal;

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }



        public async System.Threading.Tasks.Task<System.IO.Stream> GetStreamAsync(Uri requestUriTmp)

        {

            using (var client = new System.Net.Http.HttpClient())

            {

                using (var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, requestUriTmp))

                {

                    request.Headers.Add("Authorization", "Bearer " + await _accessTokenGetter());

                    request.Headers.Add("Accept", "*/*");

                    request.Headers.Add("Accept-Charset", "UTF-8");

                    request.Headers.Add("X-ClientService-ClientTag", XClientStringClientTag);



                    // Do not dispose the response. If disposed, it will also dispose the

                    // stream we are returning

                    var response = await client.SendAsync(request);

                    if (response.IsSuccessStatusCode)

                    {

                        return await response.Content.ReadAsStreamAsync();

                    }



                    var newException = await ProcessErrorAsync(response);



                    if (newException != null)

                    {

                        throw newException;

                    }



                    response.EnsureSuccessStatusCode();



                    // unreachable

                    return null;

                }

            }

        }



        public async System.Threading.Tasks.Task<DataServiceStreamResponse> GetReadStreamAsync(EntityBase entity, string streamName, string contentType)

        {

            try

            {

                await SetToken();



                if (!string.IsNullOrEmpty(streamName))

                {

                    var resp = await global::System.Threading.Tasks.Task.Factory.FromAsync<object, string, DataServiceRequestArgs, DataServiceStreamResponse>(

                        BeginGetReadStream,

                        EndGetReadStream,

                        entity,

                        streamName,

                        new DataServiceRequestArgs { ContentType = contentType /*, Headers = {todo}*/ },

                        null);

                    return resp;

                }

                else

                {

                    var resp = await global::System.Threading.Tasks.Task.Factory.FromAsync<object, DataServiceRequestArgs, DataServiceStreamResponse>(

                        BeginGetReadStream,

                        EndGetReadStream,

                        entity,

                        new DataServiceRequestArgs { ContentType = contentType /*, Headers = {todo}*/ },

                        null);



                    return resp;

                }



            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }



        public async System.Threading.Tasks.Task<IPagedCollection<TInterface>> ExecuteAsync<TSource, TInterface>(DataServiceQuery<TSource> inner) where TSource : TInterface

        {

            try

            {

                await SetToken();

                return await global::System.Threading.Tasks.Task.Factory.FromAsync<

                            IPagedCollection<TInterface>>(inner.BeginExecute,

                            new global::System.Func<global::System.IAsyncResult, IPagedCollection<TInterface>>(

                                r =>

                                {

                                    var innerResult = (QueryOperationResponse<TSource>)inner.EndExecute(r);





                                    return new PagedCollection<TInterface, TSource>(this, innerResult);

                                }

                                ), global::System.Threading.Tasks.TaskCreationOptions.None);

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }



        public new global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T>> ExecuteAsync<T>(

            global::System.Uri uri,

            string httpMethod,

            bool singleResult,

            params OperationParameter[] operationParameters)

        {

            return ExecuteAsyncInternal<T>(uri, httpMethod, singleResult, (System.IO.Stream)null, operationParameters);

        }



        public global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T>> ExecuteAsync<T>(

            global::System.Uri uri,

            string httpMethod,

            bool singleResult,

            System.IO.Stream stream,

            params OperationParameter[] operationParameters)

        {

            return ExecuteAsyncInternal<T>(uri, httpMethod, singleResult, stream ?? new System.IO.MemoryStream(), operationParameters);

        }



        public async global::System.Threading.Tasks.Task<global::System.Collections.Generic.IEnumerable<T>> ExecuteAsyncInternal<T>(

            global::System.Uri uri,

            string httpMethod,

            bool singleResult,

            System.IO.Stream stream,

            params OperationParameter[] operationParameters)

        {

            try

            {

                await SetToken();



                if (stream != null)

                {

                    Configurations.RequestPipeline.OnMessageCreating = (global::Microsoft.OData.Client.DataServiceClientRequestMessageArgs args) =>

                    {

                        args.Headers.Remove("Content-Length");



                        var msg = new global::Microsoft.OData.Client.HttpWebRequestMessage(args);



                        global::System.Threading.Tasks.Task.Factory.FromAsync<System.IO.Stream>(msg.BeginGetRequestStream, msg.EndGetRequestStream, null).ContinueWith

                            (s => stream.CopyTo(s.Result))

                            .Wait();



                        return msg;

                    };

                }



                return await global::System.Threading.Tasks.Task.Factory.FromAsync<global::System.Collections.Generic.IEnumerable<T>>

                (

                    (callback, state) => BeginExecute<T>(uri, callback, state, httpMethod, singleResult, operationParameters),

                    EndExecute<T>, global::System.Threading.Tasks.TaskCreationOptions.None);

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

            finally

            {

                if (stream != null)

                {

                    Configurations.RequestPipeline.OnMessageCreating = null;

                }

            }

        }



        public new global::System.Threading.Tasks.Task ExecuteAsync(

            global::System.Uri uri,

            string httpMethod,

            params OperationParameter[] operationParameters)

        {

            return ExecuteAsync(uri, httpMethod, (System.IO.Stream)null, operationParameters);

        }



        public async global::System.Threading.Tasks.Task ExecuteAsync(

            global::System.Uri uri,

            string httpMethod,

            System.IO.Stream stream,

            params OperationParameter[] operationParameters

            )

        {

            try

            {

                await SetToken();

                if (stream != null)

                {

                    Configurations.RequestPipeline.OnMessageCreating = (global::Microsoft.OData.Client.DataServiceClientRequestMessageArgs args) =>

                    {

                        args.Headers.Remove("Content-Length");



                        var msg = new global::Microsoft.OData.Client.HttpWebRequestMessage(args);



                        global::System.Threading.Tasks.Task.Factory.FromAsync<System.IO.Stream>(msg.BeginGetRequestStream, msg.EndGetRequestStream, null).ContinueWith

                            (s => stream.CopyTo(s.Result))

                            .Wait();



                        return msg;

                    };

                }



                await global::System.Threading.Tasks.Task.Factory.FromAsync 

                        (

                            new global::System.Func<global::System.AsyncCallback, object, global::System.IAsyncResult>(

                                (callback, state) => BeginExecute(uri, callback, state, httpMethod, operationParameters)),

                            new global::System.Action<global::System.IAsyncResult>((i) => EndExecute(i)),

                            global::System.Threading.Tasks.TaskCreationOptions.None);

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

            finally

            {

                if (stream != null)

                {

                    Configurations.RequestPipeline.OnMessageCreating = null;

                }

            }

        }



        public async System.Threading.Tasks.Task<QueryOperationResponse<TSource>> ExecuteAsync<TSource, TInterface>(DataServiceQueryContinuation<TSource> token)

        {

            try

            {

                await SetToken();



                return await global::System.Threading.Tasks.Task.Factory.FromAsync<QueryOperationResponse<TSource>>(

                    (callback, state) => BeginExecute(token, callback, state),

                    (i) => (QueryOperationResponse<TSource>)EndExecute<TSource>(i),

                    global::System.Threading.Tasks.TaskCreationOptions.None);

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }



        public async new System.Threading.Tasks.Task<DataServiceResponse> SaveChangesAsync(SaveChangesOptions options)

        {

            try

            {

                await SetToken();

                var result = await global::System.Threading.Tasks.Task.Factory.FromAsync<SaveChangesOptions, DataServiceResponse>(

                    BeginSaveChanges,

                    new global::System.Func<global::System.IAsyncResult, DataServiceResponse>(EndSaveChanges),

                    options,

                    null,

                    global::System.Threading.Tasks.TaskCreationOptions.None);



                foreach (var i in _modifiedEntities)

                {

                    i.ResetChanges();

                }



                _modifiedEntities.Clear();

                return result;

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }



        public new System.Threading.Tasks.Task<DataServiceResponse> SaveChangesAsync()

        {

            return SaveChangesAsync(SaveChangesOptions.None);

        }



#if NOTYET

        public async System.Threading.Tasks.Task<IPagedCollection<TSource>> LoadPropertyAsync<TSource>(string path, object entity)

        {

            try

            {

                await SetToken();

                return await global::System.Threading.Tasks.Task.Factory.FromAsync<

                    IPagedCollection<TSource>>(

                    (AsyncCallback callback, object state) =>

                    {

                        return BeginLoadProperty(entity, path, callback, state);

                    },

                    new global::System.Func<global::System.IAsyncResult, IPagedCollection<TSource>>(

                        r =>

                        {

                            var innerResult = (QueryOperationResponse<TSource>)EndLoadProperty(r);



                            return new PagedCollection<TSource>(this, innerResult);

                        }

                        ), global::System.Threading.Tasks.TaskCreationOptions.None);

            }

            catch (Exception ex)

            {

                var newException = ProcessException(ex);



                if (newException != null)

                {

                    throw newException;

                }



                throw;

            }

        }

#endif



        internal static Exception ProcessException(Exception ex)

        {



            if (ex is DataServiceRequestException)

            {

                var response = ((DataServiceRequestException)ex).Response.FirstOrDefault();



                if (response != null)

                {

                    return ProcessError((DataServiceRequestException)ex, ex.InnerException as DataServiceClientException, response.Headers);

                }

            }



            if (ex is DataServiceQueryException)

            {

                return ProcessError((DataServiceQueryException)ex, ex.InnerException as DataServiceClientException, ((DataServiceQueryException)ex).Response.Headers);

            }



            if (ex is DataServiceClientException)

            {

                return ProcessError(ex, (DataServiceClientException)ex, new Dictionary<string, string> { {"Content-Type", ex.Message.StartsWith("<") ? "application/xml" : "application/json"}});

            }



            return null;

        }





        private static Exception ProcessError(Exception outer, DataServiceClientException inner, IDictionary<string, string> headers)

        {

            if (inner == null)

            {

                return null;

            }



            using (var writer = WriteToStream(inner.Message))

            {

                var httpMessage = new HttpWebResponseMessage(

                    headers,

                inner.StatusCode,

                () => writer.BaseStream);



                var reader = new ODataMessageReader(httpMessage);



                try

                {

                    var error = reader.ReadError();

                    return new ODataErrorException(error.Message, outer, error);

                }

                catch

                {

                }

            }



            return null;

        }



        private static async System.Threading.Tasks.Task<Exception> ProcessErrorAsync(System.Net.Http.HttpResponseMessage response)

        {



            if (response.Content == null)

            {

                return null;

            }



            if (response.Content.Headers.ContentType == null)

            {

                return new System.Net.Http.HttpRequestException(await response.Content.ReadAsStringAsync());

            }

            

            using (var stream = await response.Content.ReadAsStreamAsync())

            {

                var headers = Enumerable.ToDictionary(response.Content.Headers, i => i.Key, i => i.Value.FirstOrDefault());



                var httpMessage = new HttpWebResponseMessage(

                  headers,

                (int)response.StatusCode,

                () => stream);



                var reader = new ODataMessageReader(httpMessage);



                try

                {

                    var error = reader.ReadError();

                    return new ODataErrorException(error.Message, null, error);

                }

                catch

                {

                }

            }



            return null;

        }



        private static System.IO.StreamWriter WriteToStream(string contents)

        {

            var stream = new System.IO.MemoryStream();

            var writer = new System.IO.StreamWriter(stream);

            writer.Write(contents);

            writer.Flush();

            stream.Seek(0, System.IO.SeekOrigin.Begin);

            return writer;

        }

    }



    public interface IBatchElementResult

    {

        IPagedCollection SuccessResult { get; }

        Exception FailureResult { get; }

    }



    class BatchElementResult : IBatchElementResult

    {



        public BatchElementResult(IPagedCollection successResult)

        {

            SuccessResult = successResult;

        }



        public BatchElementResult(Exception failureResult)

        {

            FailureResult = failureResult;

        }



        public IPagedCollection SuccessResult

        {

            get;

            private set;

        }



        public Exception FailureResult

        {

            get;

            private set;

        }

    }



    public class ComplexTypeBase 

    {

        private Func<Tuple<EntityBase, string>> _entity;



        protected ComplexTypeBase()

        {

        }



        internal virtual void SetContainer(Func<Tuple<EntityBase, string>> entity)

        {

            _entity = entity;

        }



        protected Tuple<EntityBase, string> GetContainingEntity(string propertyName)

        {

            return _entity != null ? _entity() : null;

        }



        protected void OnPropertyChanged(string propertyName)

        {

            var tuple = GetContainingEntity(propertyName);



            if (tuple != null)

            {

                tuple.Item1.OnPropertyChanged(tuple.Item2);

            }

        }

    }



    internal class EntityCollectionImpl<T> : DataServiceCollection<T> where T : EntityBase

    {

        private Func<Tuple<EntityBase, string>> _entity;



        public EntityCollectionImpl()

            : base(null, TrackingMode.None)

        {

        }



        internal void SetContainer(Func<Tuple<EntityBase, string>> entity)

        {

            _entity = entity;

        }



        protected override void InsertItem(int index, T item)

        {

            InvokeOnEntity(tuple => tuple.Item1.Context.AddRelatedObject(tuple.Item1, tuple.Item2, item));



            base.InsertItem(index, item);

        }



        protected override void ClearItems()

        {

            InvokeOnEntity(tuple => 

            {

                foreach (var i in this)

                {

                    tuple.Item1.Context.DeleteLink(tuple.Item1, tuple.Item2, i);

                }

            });



            base.ClearItems();

        }



        protected override void RemoveItem(int index)

        {

            InvokeOnEntity(tuple => tuple.Item1.Context.DeleteLink(tuple.Item1, tuple.Item2, this[index]));



            base.RemoveItem(index);

        }



        protected override void SetItem(int index, T item)

        {

            InvokeOnEntity(tuple =>

                    {

                        tuple.Item1.Context.DeleteLink(tuple.Item1, tuple.Item2, this[index]);

                        tuple.Item1.Context.AddRelatedObject(tuple.Item1, tuple.Item2, item);

                    }

            );



            base.SetItem(index, item);

        }



        private void InvokeOnEntity(Action<Tuple<EntityBase, string>> action)

        {

            if (_entity != null)

            {

                var tuple = _entity();



                if (tuple.Item1.Context != null && tuple.Item1.Context.GetEntityDescriptor(tuple.Item1) != null)

                {

                    action(tuple);

                }

            }

        }

    }



    internal class NonEntityTypeCollectionImpl<T> : global::System.Collections.ObjectModel.Collection<T>

    {

        private Func<Tuple<EntityBase, string>> _entity;



        static readonly bool _isComplexType = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(T)).IsSubclassOf(typeof(ComplexTypeBase));



        public NonEntityTypeCollectionImpl()

            : base()

        {

        }



        internal void SetContainer(Func<Tuple<EntityBase, string>> entity)

        {

            _entity = entity;



            if (_isComplexType)

            {

                foreach (var i in this)

                {

                    (i as ComplexTypeBase).SetContainer(entity);

                }

            }

        }



        protected override void InsertItem(int index, T item)

        {

            var ct = item as ComplexTypeBase;

            if (ct != null)

            {

                ct.SetContainer(_entity);

            }



            base.InsertItem(index, item);



            InvokeOnPropertyChanged();

        }



        protected override void ClearItems()

        {

            base.ClearItems();

            InvokeOnPropertyChanged();

        }



        protected override void RemoveItem(int index)

        {

            base.RemoveItem(index);

            InvokeOnPropertyChanged();

        }



        protected override void SetItem(int index, T item)

        {

            var ct = item as ComplexTypeBase;

            if (ct != null)

            {

                ct.SetContainer(_entity);

            }

            base.SetItem(index, item);

            InvokeOnPropertyChanged();

        }



        private void InvokeOnPropertyChanged()

        {

            var tuple = _entity != null ? _entity() : null;

            if (tuple != null)

            {

                tuple.Item1.OnPropertyChanged(tuple.Item2);

            }

        }

    }



    public class EntityBase  : BaseEntityType

    {

        private Lazy<HashSet<string>> _changedProperties = new Lazy<HashSet<string>>(true);



        internal Lazy<HashSet<string>> ChangedProperties

        {

            get { return _changedProperties; }

        }



        protected Tuple<EntityBase, string> GetContainingEntity(string propertyName)

        {

            return new Tuple<EntityBase, string> (this, propertyName);

        }



        protected internal void OnPropertyChanged([global::System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)

        {

            _changedProperties.Value.Add(propertyName);

            if (Context != null)

            {

                Context.UpdateObject(this);

            }

        }



        internal void ResetChanges()

        {

            _changedProperties = new Lazy<HashSet<string>>(true);

        }



        internal new DataServiceContextWrapper Context 

        { 

            get

            {

                return (DataServiceContextWrapper)base.Context;

            }

            private set

            {

                base.Context = value;

            }

        }



        internal void Initialize()

        {

        }



        protected string GetPath(string propertyName)

        {

            Uri uri = GetUrl();

            if (uri != null)

            {

                return uri.ToString().Substring(Context.BaseUri.ToString().Length + 1) + "/" + propertyName;

            }



            return null;

        }



        protected System.Uri GetUrl()

        {

            if (Context == null)

            {

                return null;

            }



            Uri uri;

            Context.TryGetUri(this, out uri);



            return uri;

        }



		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        public global::System.Threading.Tasks.Task UpdateAsync(bool dontSave = false)

        {

			 if (Context == null) throw new InvalidOperationException("Not Initialized");

            Context.UpdateObject(this);

            return SaveAsNeeded(dontSave);

        }



		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        public global::System.Threading.Tasks.Task DeleteAsync(bool dontSave = false)

        {

			 if (Context == null) throw new InvalidOperationException("Not Initialized");

            Context.DeleteObject(this);

            return SaveAsNeeded(dontSave);

        }



		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        protected internal global::System.Threading.Tasks.Task SaveAsNeeded(bool dontSave)

        {

            if (!dontSave)

            {

                return Context.SaveChangesAsync();

            }

            else

            {

                var retVal = new global::System.Threading.Tasks.TaskCompletionSource<object>();

                retVal.SetResult(null);

                return retVal.Task;

            }

        }

    }



    public class QueryableSet<TSource> : ReadOnlyQueryableSetBase<TSource>

    {

        protected string _path;

        protected object _entity;



        internal void SetContainer(Func<EntityBase> entity, string property)

        {

            // Unneeded

        }



        protected System.Uri GetUrl()

        {

            return new Uri(Context.BaseUri.ToString().TrimEnd('/') + "/" + _path);

        }



        internal QueryableSet(

            DataServiceQuery inner,

            DataServiceContextWrapper context,

            EntityBase entity,

            string path)

            : base(inner, context)

        {

            Initialize(inner, context, entity, path);

        }



        internal void Initialize(DataServiceQuery inner,

            DataServiceContextWrapper context,

            EntityBase entity,

            string path)

        {

            base.Initialize(inner, context);

            _path = path;

            _entity = entity;

        }

    }



    public interface IReadOnlyQueryableSet<TSource> : IReadOnlyQueryableSetBase<TSource>

    {

        System.Threading.Tasks.Task<IPagedCollection<TSource>> ExecuteAsync();

        System.Threading.Tasks.Task<TSource> ExecuteSingleAsync();

    }

    

    public class ReadOnlyQueryableSet<TSource> : ReadOnlyQueryableSetBase<TSource>, IReadOnlyQueryableSet<TSource>

    {

        internal ReadOnlyQueryableSet(

            DataServiceQuery inner,

            DataServiceContextWrapper context)

            :base (inner, context)

        {

        }





        public global::System.Threading.Tasks.Task<IPagedCollection<TSource>> ExecuteAsync()

        {

            return base.ExecuteAsyncInternal();

        }



        public global::System.Threading.Tasks.Task<TSource> ExecuteSingleAsync()

        {

            return base.ExecuteSingleAsyncInternal();

        }

    }



    public interface IReadOnlyQueryableSetBase

    {

        DataServiceContextWrapper Context { get; }

        DataServiceQuery Query { get; }

    }



    public interface IReadOnlyQueryableSetBase<TSource> : IReadOnlyQueryableSetBase

    {

        IReadOnlyQueryableSet<TSource> Expand<TTarget>(System.Linq.Expressions.Expression<Func<TSource, TTarget>> navigationPropertyAccessor);

        IReadOnlyQueryableSet<TResult> OfType<TResult>();

        IReadOnlyQueryableSet<TSource> OrderBy<TKey>(System.Linq.Expressions.Expression<Func<TSource, TKey>> keySelector);

        IReadOnlyQueryableSet<TSource> OrderByDescending<TKey>(System.Linq.Expressions.Expression<Func<TSource, TKey>> keySelector);

        IReadOnlyQueryableSet<TResult> Select<TResult>(System.Linq.Expressions.Expression<Func<TSource, TResult>> selector);

        IReadOnlyQueryableSet<TSource> Skip(int count);

        IReadOnlyQueryableSet<TSource> Take(int count);

        IReadOnlyQueryableSet<TSource> Where(System.Linq.Expressions.Expression<Func<TSource, bool>> predicate);

    }



    public interface IConcreteTypeAccessor

    {

        Type ConcreteType { get; }

        Type ElementType { get; }

    }



    public abstract class ReadOnlyQueryableSetBase<TSource> :IReadOnlyQueryableSetBase<TSource>, IConcreteTypeAccessor

    {

        protected DataServiceQuery _inner;

        protected DataServiceContextWrapper _context;



        private Lazy<Type> _concreteType = new Lazy<Type>(() => CreateConcreteType(typeof(TSource)), true);



        // Will return null if not an interface

        private static Type CreateConcreteType(Type tsourceType)

        {

            var tsourceTypeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(tsourceType);

            if (tsourceTypeInfo.IsGenericType)

            {

                var arguments = tsourceTypeInfo.GenericTypeArguments;

                bool modified = false;



                for(int i = 0; i < arguments.Length; i++)

                {

                    var converted = CreateConcreteType(arguments[i]);

                    if (converted != null)

                    {

                        arguments[i] = converted;

                        modified = true;

                    }

                }



                if (!modified)

                {

                    return null;

                }



                // Properties declared as IPagedCollection on the interface are declared as IList on the concrete type

                if (tsourceTypeInfo.GetGenericTypeDefinition() == typeof(IPagedCollection<>))

                {

                    return typeof(IList<>).MakeGenericType(arguments);

                }



                return tsourceTypeInfo.GetGenericTypeDefinition().MakeGenericType(arguments);

            }



            const string Fetcher = "Fetcher";

            if (System.Linq.Enumerable.Any<System.Reflection.CustomAttributeData>(

                tsourceTypeInfo.CustomAttributes,

                i => i.AttributeType == typeof(LowerCasePropertyAttribute)))

            {

                string typeName = tsourceTypeInfo.Namespace + "." + tsourceTypeInfo.Name.Substring(1);

                if (typeName.EndsWith(Fetcher))

                {

                    typeName = typeName.Substring(typeName.Length - Fetcher.Length);

                }

                return tsourceTypeInfo.Assembly.GetType(typeName);

            }

            else

            {

                return null;

            }

        }



        public DataServiceContextWrapper Context

        {

            get { return _context; }

        }



        public DataServiceQuery Query

        {

            get { return _inner; }

        }



        public ReadOnlyQueryableSetBase(

            DataServiceQuery inner,

            DataServiceContextWrapper context)

        {

            Initialize(inner, context);        

        }



        protected void Initialize(DataServiceQuery inner,

            DataServiceContextWrapper context)

        {

            _inner = inner;

            _context = context;

        }



#region IConcreteTypeAccessor implementation



        Type IConcreteTypeAccessor.ConcreteType

        {

            get

            {

                return _concreteType.Value ?? typeof(TSource);

            }

        }



        Type IConcreteTypeAccessor.ElementType

        {

            get

            {

                return typeof(TSource);

            }

        }



#endregion



        protected global::System.Threading.Tasks.Task<IPagedCollection<TSource>> ExecuteAsyncInternal()

        {



            if (_concreteType.Value != null)

            {

                var contextTypeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(DataServiceContextWrapper));



                var executeAsyncMethodInfo =

                    (from i in contextTypeInfo.GetDeclaredMethods("ExecuteAsync")

                     let parameters = i.GetParameters()

                     where parameters.Length == 1 && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(DataServiceQuery<>)

                     select i).First();



                return (global::System.Threading.Tasks.Task<IPagedCollection<TSource>>) 

                    executeAsyncMethodInfo.MakeGenericMethod(_concreteType.Value, typeof(TSource)).Invoke(_context, new [] { _inner });

            }

            else

            {

                return _context.ExecuteAsync<TSource, TSource>((DataServiceQuery<TSource>)_inner);

            }

        }



        protected global::System.Threading.Tasks.Task<TSource> ExecuteSingleAsyncInternal()

        {

            if (_concreteType.Value != null)

            {

                var contextTypeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(DataServiceContextWrapper));



                var executeAsyncMethodInfo =

                    (from i in contextTypeInfo.GetDeclaredMethods("ExecuteSingleAsync")

                     let parameters = i.GetParameters()

                     where parameters.Length == 1 && parameters[0].ParameterType.GetGenericTypeDefinition() == typeof(DataServiceQuery<>)

                     select i).First();



                return (global::System.Threading.Tasks.Task<TSource>)

                    executeAsyncMethodInfo.MakeGenericMethod(_concreteType.Value, typeof(TSource)).Invoke(_context, new[] { _inner });

            }

            else

            {

                return _context.ExecuteSingleAsync<TSource, TSource>((DataServiceQuery<TSource>)_inner);

            }

        }



        #region LINQ





        private class PascalCaseExpressionVisitor : System.Linq.Expressions.ExpressionVisitor

        {

            Dictionary<System.Linq.Expressions.ParameterExpression, System.Linq.Expressions.ParameterExpression>

                _parameterDictionary = new Dictionary<System.Linq.Expressions.ParameterExpression, System.Linq.Expressions.ParameterExpression>();



            protected override System.Linq.Expressions.Expression VisitExtension(System.Linq.Expressions.Expression node)

            {

                return node;

            }



            protected override System.Linq.Expressions.Expression VisitLambda<T>(System.Linq.Expressions.Expression<T> node)

            {



                var originalDelegateType = typeof(T);



                if (originalDelegateType.GetGenericTypeDefinition() == typeof(Func<,>))

                {

                    var newParameterArray = System.Reflection.IntrospectionExtensions.GetTypeInfo(originalDelegateType).GenericTypeArguments;

                    bool hasInterfaces = false;



                    var ct = CreateConcreteType(newParameterArray[0]);

                    if (ct != null)

                    {

                        hasInterfaces = true;

                        newParameterArray[0] = ct;

                    }



                    ct = CreateConcreteType(newParameterArray[1]);

                    if (ct != null)

                    {

                        hasInterfaces = true;

                        newParameterArray[1] = ct;

                    }



                    if (!hasInterfaces)

                    {

                        return base.VisitLambda(node);

                    }



                    var newdDelegateType = typeof(Func<,>).MakeGenericType(newParameterArray);



                    var invocationParameters = node.Parameters.ToArray();



                    for (int i = 0; i < invocationParameters.Length; i++)

                    {

                        var concreteType = CreateConcreteType(invocationParameters[i].Type);



                        if (concreteType != null)

                        {

                            if (!_parameterDictionary.ContainsKey(invocationParameters[i]))

                            {

                                _parameterDictionary[invocationParameters[i]] =  System.Linq.Expressions.Expression.Parameter(

                                concreteType, invocationParameters[i].Name);

                            }



                            invocationParameters[i] = _parameterDictionary[invocationParameters[i]];

                        }

                    }



                    var body = Visit(node.Body);



                    var newLambda = System.Linq.Expressions.Expression.Lambda(

                        newdDelegateType,

                        body,

                        node.TailCall,

                        invocationParameters);



                    return newLambda;

                }



                return base.VisitLambda<T>(node);

            }



            protected override System.Linq.Expressions.Expression VisitParameter(System.Linq.Expressions.ParameterExpression node)

            {

                var concreteType = CreateConcreteType(node.Type);



                if (concreteType == null)

                {

                    return base.VisitParameter(node);

                }



                if (!_parameterDictionary.ContainsKey(node))

                {

                    _parameterDictionary[node] = System.Linq.Expressions.Expression.Parameter(

                    concreteType,

                    node.Name);

                }



                return base.VisitParameter(_parameterDictionary[node]);

            }



            protected override System.Linq.Expressions.Expression VisitMember(System.Linq.Expressions.MemberExpression node)

            {

                if (node.Member is System.Reflection.PropertyInfo)

                {

                    var interfaceType = CreateConcreteType(node.Type) != null;



                    var toLower = System.Linq.Enumerable.Any(

                        node.Member.CustomAttributes, i => i.AttributeType == typeof(LowerCasePropertyAttribute));



                    if (interfaceType || toLower)

                    {

                        var newExpression = Visit(node.Expression);



                        return base.VisitMember(

                            System.Linq.Expressions.Expression.Property(

                                newExpression,

                                System.Reflection.RuntimeReflectionExtensions.GetRuntimeProperty(

                                    newExpression.Type,

                                    toLower ? char.ToLower(node.Member.Name[0]) + node.Member.Name.Substring(1) : node.Member.Name

                               )

                            )

                        );

                    }

                }

                /*

                    Example - "me" is a field:



                    var me = await client.Me.ExecuteAsync();

            

                    var filesQuery = await client.Users.Where(i => i.UserPrincipalName != me.UserPrincipalName).ExecuteAsync();

                */

                else if (node.Member is System.Reflection.FieldInfo) // for local variables

                {

                    var fieldTypeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(((System.Reflection.FieldInfo)node.Member).FieldType);

                    if (System.Linq.Enumerable.Any<System.Reflection.CustomAttributeData>(fieldTypeInfo.CustomAttributes, i => i.AttributeType == typeof(LowerCasePropertyAttribute)))

                    {

                        var expression = System.Linq.Expressions.Expression.TypeAs(node, CreateConcreteType(fieldTypeInfo.AsType()));

                        return expression;

                    }

                }



                return base.VisitMember(node);

            }

        }



        private System.Linq.Expressions.ExpressionVisitor _pascalCaseExpressionVisitor = new PascalCaseExpressionVisitor();



        public IReadOnlyQueryableSet<TResult> Select<TResult>(System.Linq.Expressions.Expression<System.Func<TSource, TResult>> selector)

        {

            var newSelector = _pascalCaseExpressionVisitor.Visit(selector);



            DataServiceQuery query = CallLinqMethod(newSelector); 

            

            return new ReadOnlyQueryableSet<TResult>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TSource> Where(System.Linq.Expressions.Expression<System.Func<TSource, bool>> predicate)

        {

            // Fix for DevDiv 941323:

            if (predicate.Body.NodeType == System.Linq.Expressions.ExpressionType.Coalesce)

            {

                var binary = (System.Linq.Expressions.BinaryExpression)predicate.Body;



                var constantRight = binary.Right as System.Linq.Expressions.ConstantExpression;



                // If we are coalescing bool to false, it is a no-op

                if (constantRight != null && 

                    constantRight.Value is bool && 

                    !(bool)constantRight.Value && 

                    binary.Left.Type == typeof (bool?) &&

                    binary.Left is System.Linq.Expressions.BinaryExpression)

                {

                    var oldLeft = (System.Linq.Expressions.BinaryExpression)binary.Left;



                    var newLeft = System.Linq.Expressions.Expression.MakeBinary(

                        oldLeft.NodeType,

                        oldLeft.Left,

                        oldLeft.Right);



                    predicate = (System.Linq.Expressions.Expression<System.Func<TSource, bool>>)System.Linq.Expressions.Expression.Lambda(

                        predicate.Type,

                        newLeft,

                        predicate.TailCall,

                        predicate.Parameters);

                }

            }                   



            var newSelector = _pascalCaseExpressionVisitor.Visit(predicate);



            DataServiceQuery query = CallLinqMethod(newSelector, true); 



            return new ReadOnlyQueryableSet<TSource>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TResult> OfType<TResult>()

        {

            DataServiceQuery query = ApplyLinq(new[] { typeof(TResult) }, new object[] { _inner }) ??

                (DataServiceQuery)System.Linq.Queryable.OfType<TResult>((System.Linq.IQueryable<TSource>)_inner);



            return new ReadOnlyQueryableSet<TResult>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TSource> Skip(int count)

        {

            DataServiceQuery query = ApplyLinq(new[] { typeof(TSource) }, new object[] { _inner, count }) ??

                (DataServiceQuery)System.Linq.Queryable.Skip<TSource>((System.Linq.IQueryable<TSource>)_inner, count);



            return new ReadOnlyQueryableSet<TSource>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TSource> Take(int count)

        {

            DataServiceQuery query = ApplyLinq(new[] { typeof(TSource) }, new object[] { _inner, count }) ??

                (DataServiceQuery)System.Linq.Queryable.Take<TSource>((System.Linq.IQueryable<TSource>)_inner, count);



            return new ReadOnlyQueryableSet<TSource>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TSource> OrderBy<TKey>(System.Linq.Expressions.Expression<System.Func<TSource, TKey>> keySelector)

        {

            var newSelector = _pascalCaseExpressionVisitor.Visit(keySelector);



            DataServiceQuery query = CallLinqMethod(newSelector); 



            return new ReadOnlyQueryableSet<TSource>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TSource> OrderByDescending<TKey>(System.Linq.Expressions.Expression<System.Func<TSource, TKey>> keySelector)

        {

            var newSelector = _pascalCaseExpressionVisitor.Visit(keySelector);



            DataServiceQuery query = CallLinqMethod(newSelector); 



            return new ReadOnlyQueryableSet<TSource>(

                query,

                _context);

        }



        public IReadOnlyQueryableSet<TSource> Expand<TTarget>(System.Linq.Expressions.Expression<Func<TSource, TTarget>> navigationPropertyAccessor)

        {

            var newSelector = _pascalCaseExpressionVisitor.Visit(navigationPropertyAccessor);



            var concreteType = _concreteType.Value ?? typeof(TSource);

            var concreteDsq = typeof(DataServiceQuery<>).MakeGenericType(concreteType);



            DataServiceQuery query = CallOnConcreteType(concreteDsq, _inner, new[] { typeof(TTarget) }, new object[] { newSelector });



            return new ReadOnlyQueryableSet<TSource>(

                query,

                _context);

        }



        private DataServiceQuery ApplyLinq(Type[] typeParams, object[] callParams, [System.Runtime.CompilerServices.CallerMemberName] string methodName = null)

        {

            return CallOnConcreteType(typeof(System.Linq.Queryable), null, typeParams, callParams, methodName);

        }



        private DataServiceQuery CallOnConcreteType(Type targetType, object instance, Type[] typeParams, object[] callParams, [System.Runtime.CompilerServices.CallerMemberName] string methodName = null)

        {

            for (int i = 0; i < typeParams.Length; i++)

            {

                if (typeParams[i] == typeof(TSource))

                {

                    typeParams[i] = _concreteType.Value;

                }

                else

                {

                    var concreteType = CreateConcreteType(typeParams[i]);



                    if (concreteType != null)

                    {

                        typeParams[i] = concreteType;

                    }

                }

            }



            var typeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(targetType);

            var methodInfo =

                (from i in typeInfo.GetDeclaredMethods(methodName)

                 let parameters = i.GetParameters()

                 where i.GetGenericArguments().Length == typeParams.Length

                 let constructedMethod = i.MakeGenericMethod(typeParams)

                 where AllParametersAreAssignable(constructedMethod.GetParameters(), callParams)

                 select constructedMethod).First();



            return (DataServiceQuery)methodInfo.Invoke(instance, callParams);

        }



        private bool AllParametersAreAssignable(System.Reflection.ParameterInfo[] parameterInfo, object[] callParams)

        {

            for (int i = 0; i < parameterInfo.Length; i++)

            {

                if (callParams[i] != null &&

                    !System.Reflection.IntrospectionExtensions.GetTypeInfo(parameterInfo[i].ParameterType).IsAssignableFrom(

                    System.Reflection.IntrospectionExtensions.GetTypeInfo(callParams[i].GetType())))

                {

                    return false;

                }

            }



            return true;

        }



        private DataServiceQuery CallLinqMethod(

            System.Linq.Expressions.Expression predicate,

            bool singleGenericParameter = false,

            [System.Runtime.CompilerServices.CallerMemberName] string methodName = null)

        {

            System.Type[] typeParams = singleGenericParameter ?

                new Type[] { predicate.Type.GenericTypeArguments[0] } : 

                predicate.Type.GenericTypeArguments;



            var callParams = new object[] { _inner, predicate };



            var typeInfo = System.Reflection.IntrospectionExtensions.GetTypeInfo(typeof(System.Linq.Queryable));

            var methodInfo =

                (from i in typeInfo.GetDeclaredMethods(methodName)

                 let parameters = i.GetParameters()

                 where i.GetGenericArguments().Length == typeParams.Length

                 let constructedMethod = i.MakeGenericMethod(typeParams)

                 where AllParametersAreAssignable(constructedMethod.GetParameters(), callParams)

                 select constructedMethod).First();



            return (DataServiceQuery)methodInfo.Invoke(null, callParams);

        }



        #endregion

    }



    public interface IPagedCollection<TElement>

    {

        global::System.Collections.Generic.IReadOnlyList<TElement> CurrentPage { get; }

        bool MorePagesAvailable { get; }

        global::System.Threading.Tasks.Task<IPagedCollection<TElement>> GetNextPageAsync();

    }

    

    public interface IPagedCollection

    {

        global::System.Collections.Generic.IReadOnlyList<object> CurrentPage { get; }

        bool MorePagesAvailable { get; }

        global::System.Threading.Tasks.Task<IPagedCollection> GetNextPageAsync();

    }



    internal class PagedCollection<TElement, TConcrete> : IPagedCollection, IPagedCollection<TElement> where TConcrete : TElement

    {

        private DataServiceContextWrapper _context;

        private DataServiceQueryContinuation<TConcrete> _continuation;

        private IReadOnlyList<TElement> _currentPage;



        // Creator - should be faster than Activator.CreateInstance

        public static PagedCollection<TElement, TConcrete> Create(DataServiceContextWrapper context,

            QueryOperationResponse<TConcrete> qor)

        {

            return new PagedCollection<TElement, TConcrete>(context, qor);

        }

                

        internal PagedCollection(DataServiceContextWrapper context,

            QueryOperationResponse<TConcrete> qor)

        {

            _context = context;

            _currentPage = (IReadOnlyList<TElement>)qor.ToList();

            _continuation = qor.GetContinuation();

        }



        public PagedCollection(DataServiceContextWrapper context, DataServiceCollection<TConcrete> collection)

        {

            _context = context;

            _currentPage = (IReadOnlyList<TElement>)collection;

            if (_currentPage != null)

            {

                _continuation = collection.Continuation;

            }

        }



        public bool MorePagesAvailable

        {

            get

            {

                return _continuation != null;

            }

        }



        public System.Collections.Generic.IReadOnlyList<TElement> CurrentPage

        {

            get

            {

                return _currentPage;

            }

        }



        public async System.Threading.Tasks.Task<IPagedCollection<TElement>> GetNextPageAsync()

        {

            if (_continuation != null)

            {

                var task =  _context.ExecuteAsync<TConcrete, TElement>(_continuation);



                return new PagedCollection<TElement, TConcrete>(_context, await task);

            }



            return (IPagedCollection<TElement>)null;

        }



        IReadOnlyList<object> IPagedCollection.CurrentPage

        {

            get { return (IReadOnlyList<object>)this.CurrentPage; }

        }



        async System.Threading.Tasks.Task<IPagedCollection> IPagedCollection.GetNextPageAsync()

        {

            var retval = await GetNextPageAsync();



            return (PagedCollection<TElement, TConcrete>)retval;

        }

    }



    public interface IStreamFetcher

    {

        string ContentType { get; }

        global::System.Threading.Tasks.Task<global::Microsoft.OData.Client.DataServiceStreamResponse> DownloadAsync();

		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        global::System.Threading.Tasks.Task UploadAsync(global::System.IO.Stream stream, string contentType, bool dontSave = false, bool closeStream = false);

    }

        

    internal class StreamFetcher : IStreamFetcher

    {

        private global::Microsoft.OData.Client.DataServiceStreamLink _link;

        private EntityBase _entity;

        private string _propertyName;

        private DataServiceContextWrapper _context;



        public string ContentType

        {

            get

            {

                return _link.ContentType;

            }

        }



        internal StreamFetcher(DataServiceContextWrapper context, EntityBase entity, string propertyName, global::Microsoft.OData.Client.DataServiceStreamLink link)

        {

            _context = context;

            _entity = entity;

            _link = link;

            _propertyName = propertyName;

        }



		/// <param name="dontSave">true to delay saving until batch is saved; false to save immediately.</param>

        public global::System.Threading.Tasks.Task UploadAsync(global::System.IO.Stream stream, string contentType, bool dontSave = false, bool closeStream = false)

        {

            var args = new global::Microsoft.OData.Client.DataServiceRequestArgs

            {

                ContentType = contentType

            };



            if (_link.ETag != null)

            {

                args.Headers.Add("If-Match", _link.ETag);

            }



            _context.SetSaveStream(_entity, _propertyName, stream, closeStream, args);



            _entity.OnPropertyChanged(_propertyName);



            return _entity.SaveAsNeeded(dontSave);

        }



        public global::System.Threading.Tasks.Task<global::Microsoft.OData.Client.DataServiceStreamResponse> DownloadAsync()

        {

            return _context.GetReadStreamAsync(_entity, _propertyName, ContentType);

        }

    }

}

namespace Microsoft.CoreServices

{

    using global::Microsoft.OData.Client;

    using global::Microsoft.OData.Core;

    using global::Microsoft.OData.Edm;

    using System;

    using System.Collections.Generic;

    using System.ComponentModel;

    using System.Linq;

    using System.Reflection;

}

namespace Microsoft.FileServices

{

    using global::Microsoft.OData.Client;

    using global::Microsoft.OData.Core;

    using global::Microsoft.OData.Edm;

    using System;

    using System.Collections.Generic;

    using System.ComponentModel;

    using System.Linq;

    using System.Reflection;

    public partial interface IItem

    {

        Microsoft.FileServices.IdentitySet createdBy
        {get;set;}

        System.String eTag
        {get;set;}

        System.String id
        {get;set;}

        Microsoft.FileServices.IdentitySet lastModifiedBy
        {get;set;}

        System.String name
        {get;set;}

        Microsoft.FileServices.ItemReference parentReference
        {get;set;}

        System.Int64 size
        {get;set;}

        System.DateTimeOffset dateTimeCreated
        {get;set;}

        System.DateTimeOffset dateTimeLastModified
        {get;set;}

        System.String type
        {get;set;}

        System.String webUrl
        {get;set;}

    }

    [global::Microsoft.OData.Client.Key("id")]

    public abstract partial class Item:Microsoft.CoreServices.Extensions.EntityBase, Microsoft.FileServices.IItem, Microsoft.FileServices.IItemFetcher

    {

        private Microsoft.FileServices.IdentitySet _createdBy;

        private System.String _eTag;

        private System.String _id;

        private Microsoft.FileServices.IdentitySet _lastModifiedBy;

        private System.String _name;

        private Microsoft.FileServices.ItemReference _parentReference;

        private System.Int64 _size;

        private System.DateTimeOffset _dateTimeCreated;

        private System.DateTimeOffset _dateTimeLastModified;

        private System.String _type;

        private System.String _webUrl;

        public Microsoft.FileServices.IdentitySet createdBy

        {

            get

            {

                return _createdBy;

            }

            set

            {

                if (value != _createdBy)

                {

                    _createdBy = value;

                    OnPropertyChanged("createdBy");

                }

            }

        }

        public System.String eTag

        {

            get

            {

                return _eTag;

            }

            set

            {

                if (value != _eTag)

                {

                    _eTag = value;

                    OnPropertyChanged("eTag");

                }

            }

        }

        public System.String id

        {

            get

            {

                return _id;

            }

            set

            {

                if (value != _id)

                {

                    _id = value;

                    OnPropertyChanged("id");

                }

            }

        }

        public Microsoft.FileServices.IdentitySet lastModifiedBy

        {

            get

            {

                return _lastModifiedBy;

            }

            set

            {

                if (value != _lastModifiedBy)

                {

                    _lastModifiedBy = value;

                    OnPropertyChanged("lastModifiedBy");

                }

            }

        }

        public System.String name

        {

            get

            {

                return _name;

            }

            set

            {

                if (value != _name)

                {

                    _name = value;

                    OnPropertyChanged("name");

                }

            }

        }

        public Microsoft.FileServices.ItemReference parentReference

        {

            get

            {

                return _parentReference;

            }

            set

            {

                if (value != _parentReference)

                {

                    _parentReference = value;

                    OnPropertyChanged("parentReference");

                }

            }

        }

        public System.Int64 size

        {

            get

            {

                return _size;

            }

            set

            {

                if (value != _size)

                {

                    _size = value;

                    OnPropertyChanged("size");

                }

            }

        }

        public System.DateTimeOffset dateTimeCreated

        {

            get

            {

                return _dateTimeCreated;

            }

            set

            {

                if (value != _dateTimeCreated)

                {

                    _dateTimeCreated = value;

                    OnPropertyChanged("dateTimeCreated");

                }

            }

        }

        public System.DateTimeOffset dateTimeLastModified

        {

            get

            {

                return _dateTimeLastModified;

            }

            set

            {

                if (value != _dateTimeLastModified)

                {

                    _dateTimeLastModified = value;

                    OnPropertyChanged("dateTimeLastModified");

                }

            }

        }

        public System.String type

        {

            get

            {

                return _type;

            }

            set

            {

                if (value != _type)

                {

                    _type = value;

                    OnPropertyChanged("type");

                }

            }

        }

        public System.String webUrl

        {

            get

            {

                return _webUrl;

            }

            set

            {

                if (value != _webUrl)

                {

                    _webUrl = value;

                    OnPropertyChanged("webUrl");

                }

            }

        }

        public Item(): base()

        {

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IItem> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IItem>(Context.CreateQuery<Microsoft.FileServices.IItem>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IItem> _query;

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IItemFetcher

    {

    }

    internal partial class ItemFetcher:Microsoft.CoreServices.Extensions.RestShallowObjectFetcher, Microsoft.FileServices.IItemFetcher

    {

        public ItemFetcher(): base()

        {

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IItemCollection:Microsoft.CoreServices.Extensions.IReadOnlyQueryableSetBase<Microsoft.FileServices.IItem>

    {

        Microsoft.FileServices.IItemFetcher GetById(System.String id);

        global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IItem>> ExecuteAsync();

        global::System.Threading.Tasks.Task AddItemAsync(Microsoft.FileServices.IItem item, System.Boolean dontSave = false);

         Microsoft.FileServices.IItemFetcher this[System.String id]

        {get;}

    }

    internal partial class ItemCollection:Microsoft.CoreServices.Extensions.QueryableSet<Microsoft.FileServices.IItem>, Microsoft.FileServices.IItemCollection

    {

        internal ItemCollection(global::Microsoft.OData.Client.DataServiceQuery inner,Microsoft.CoreServices.Extensions.DataServiceContextWrapper context,object entity,string path): base(inner, context, entity as Microsoft.CoreServices.Extensions.EntityBase, path)

        {

        }

        public Microsoft.FileServices.IItemFetcher GetById(System.String id)

        {

            return this[id];

        }

        public global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IItem>> ExecuteAsync()

        {

            return base.ExecuteAsyncInternal();

        }

        public global::System.Threading.Tasks.Task AddItemAsync(Microsoft.FileServices.IItem item, System.Boolean dontSave = false)

        {

            if (_entity == null)

            {

                Context.AddObject(_path, item);

            }

            else

            {

                var lastSlash = _path.LastIndexOf('/');

                var shortPath = (lastSlash >= 0 && lastSlash < _path.Length - 1) ? _path.Substring(lastSlash + 1) : _path;

                Context.AddRelatedObject(_entity, shortPath, item);

            }

            if (!dontSave)

            {

                return Context.SaveChangesAsync();

            }

            else

            {

                var retVal = new global::System.Threading.Tasks.TaskCompletionSource<object>();

                retVal.SetResult(null);

                return retVal.Task;

            }

        }

        public Microsoft.FileServices.IItemFetcher this[System.String id]

        {

            get

            {

                var query = (global::Microsoft.OData.Client.DataServiceQuery)System.Linq.Queryable.Where(Context.CreateQuery<Microsoft.FileServices.IItem>(this._path), (i) => i.id == id);

                var fetcher = new Microsoft.FileServices.ItemFetcher();

                var path = query.RequestUri.ToString().Substring(Context.BaseUri.ToString().TrimEnd('/').Length + 1);

                fetcher.Initialize(Context, path);

                

                return fetcher;

            }

        }

    }

    public partial interface IDrive

    {

        System.String id
        {get;set;}

        Microsoft.FileServices.Identity owner
        {get;set;}

        Microsoft.FileServices.DriveQuota quota
        {get;set;}

    }

    [global::Microsoft.OData.Client.Key("id")]

    public partial class Drive:Microsoft.CoreServices.Extensions.EntityBase, Microsoft.FileServices.IDrive, Microsoft.FileServices.IDriveFetcher

    {

        private System.String _id;

        private Microsoft.FileServices.Identity _owner;

        private Microsoft.FileServices.DriveQuota _quota;

        public System.String id

        {

            get

            {

                return _id;

            }

            set

            {

                if (value != _id)

                {

                    _id = value;

                    OnPropertyChanged("id");

                }

            }

        }

        public Microsoft.FileServices.Identity owner

        {

            get

            {

                return _owner;

            }

            set

            {

                if (value != _owner)

                {

                    _owner = value;

                    OnPropertyChanged("owner");

                }

            }

        }

        public Microsoft.FileServices.DriveQuota quota

        {

            get

            {

                return _quota;

            }

            set

            {

                if (value != _quota)

                {

                    _quota = value;

                    OnPropertyChanged("quota");

                }

            }

        }

        public Drive(): base()

        {

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IDrive> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IDrive>(Context.CreateQuery<Microsoft.FileServices.IDrive>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IDrive> _query;

        global::System.Threading.Tasks.Task<Microsoft.FileServices.IDrive> Microsoft.FileServices.IDriveFetcher.ExecuteAsync()

        {

            var tsc = new global::System.Threading.Tasks.TaskCompletionSource<Microsoft.FileServices.IDrive>();

            tsc.SetResult(this);

            return tsc.Task;

        }

        Microsoft.FileServices.IDriveFetcher Microsoft.FileServices.IDriveFetcher.Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IDrive, TTarget>> navigationPropertyAccessor)

        {

            return (Microsoft.FileServices.IDriveFetcher) this;

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IDriveFetcher

    {

        global::System.Threading.Tasks.Task<Microsoft.FileServices.IDrive> ExecuteAsync();

        Microsoft.FileServices.IDriveFetcher Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IDrive, TTarget>> navigationPropertyAccessor);

    }

    internal partial class DriveFetcher:Microsoft.CoreServices.Extensions.RestShallowObjectFetcher, Microsoft.FileServices.IDriveFetcher

    {

        public DriveFetcher(): base()

        {

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IDrive> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IDrive>(Context.CreateQuery<Microsoft.FileServices.IDrive>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IDrive> _query;

        public async global::System.Threading.Tasks.Task<Microsoft.FileServices.IDrive> ExecuteAsync()

        {

            return await EnsureQuery().ExecuteSingleAsync();

        }

        public Microsoft.FileServices.IDriveFetcher Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IDrive, TTarget>> navigationPropertyAccessor)

        {

            return (Microsoft.FileServices.IDriveFetcher) new Microsoft.FileServices.DriveFetcher()

            {

                _query = this.EnsureQuery().Expand<TTarget>(navigationPropertyAccessor)

            }

            ;

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IDriveCollection:Microsoft.CoreServices.Extensions.IReadOnlyQueryableSetBase<Microsoft.FileServices.IDrive>

    {

        Microsoft.FileServices.IDriveFetcher GetById(System.String id);

        global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IDrive>> ExecuteAsync();

        global::System.Threading.Tasks.Task AddDriveAsync(Microsoft.FileServices.IDrive item, System.Boolean dontSave = false);

         Microsoft.FileServices.IDriveFetcher this[System.String id]

        {get;}

    }

    internal partial class DriveCollection:Microsoft.CoreServices.Extensions.QueryableSet<Microsoft.FileServices.IDrive>, Microsoft.FileServices.IDriveCollection

    {

        internal DriveCollection(global::Microsoft.OData.Client.DataServiceQuery inner,Microsoft.CoreServices.Extensions.DataServiceContextWrapper context,object entity,string path): base(inner, context, entity as Microsoft.CoreServices.Extensions.EntityBase, path)

        {

        }

        public Microsoft.FileServices.IDriveFetcher GetById(System.String id)

        {

            return this[id];

        }

        public global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IDrive>> ExecuteAsync()

        {

            return base.ExecuteAsyncInternal();

        }

        public global::System.Threading.Tasks.Task AddDriveAsync(Microsoft.FileServices.IDrive item, System.Boolean dontSave = false)

        {

            if (_entity == null)

            {

                Context.AddObject(_path, item);

            }

            else

            {

                var lastSlash = _path.LastIndexOf('/');

                var shortPath = (lastSlash >= 0 && lastSlash < _path.Length - 1) ? _path.Substring(lastSlash + 1) : _path;

                Context.AddRelatedObject(_entity, shortPath, item);

            }

            if (!dontSave)

            {

                return Context.SaveChangesAsync();

            }

            else

            {

                var retVal = new global::System.Threading.Tasks.TaskCompletionSource<object>();

                retVal.SetResult(null);

                return retVal.Task;

            }

        }

        public Microsoft.FileServices.IDriveFetcher this[System.String id]

        {

            get

            {

                var query = (global::Microsoft.OData.Client.DataServiceQuery)System.Linq.Queryable.Where(Context.CreateQuery<Microsoft.FileServices.IDrive>(this._path), (i) => i.id == id);

                var fetcher = new Microsoft.FileServices.DriveFetcher();

                var path = query.RequestUri.ToString().Substring(Context.BaseUri.ToString().TrimEnd('/').Length + 1);

                fetcher.Initialize(Context, path);

                

                return fetcher;

            }

        }

    }

    public partial class DriveQuota:Microsoft.CoreServices.Extensions.ComplexTypeBase

    {

        public DriveQuota(): base()

        {

        }

    }

    public partial class IdentitySet:Microsoft.CoreServices.Extensions.ComplexTypeBase

    {

        public IdentitySet(): base()

        {

        }

    }

    public partial class Identity:Microsoft.CoreServices.Extensions.ComplexTypeBase

    {

        public Identity(): base()

        {

        }

    }

    public partial class ItemReference:Microsoft.CoreServices.Extensions.ComplexTypeBase

    {

        public ItemReference(): base()

        {

        }

    }

    public partial interface IFile:Microsoft.FileServices.IItem

    {

        System.String contentUrl
        {get;set;}

        System.Threading.Tasks.Task<Microsoft.FileServices.IFile> copy(System.String destFolderId, System.String destFolderPath, System.String newName);

        System.Threading.Tasks.Task uploadContent(System.IO.Stream contentStream);

        System.Threading.Tasks.Task<System.IO.Stream> content();

    }

    [global::Microsoft.OData.Client.Key("id")]

    public partial class File:Microsoft.FileServices.Item, Microsoft.FileServices.IFile, Microsoft.FileServices.IFileFetcher

    {

        private System.String _contentUrl;

        public System.String contentUrl

        {

            get

            {

                return _contentUrl;

            }

            set

            {

                if (value != _contentUrl)

                {

                    _contentUrl = value;

                    OnPropertyChanged("contentUrl");

                }

            }

        }

        public File()

        {

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFile> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IFile>(Context.CreateQuery<Microsoft.FileServices.IFile>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFile> _query;

        global::System.Threading.Tasks.Task<Microsoft.FileServices.IFile> Microsoft.FileServices.IFileFetcher.ExecuteAsync()

        {

            var tsc = new global::System.Threading.Tasks.TaskCompletionSource<Microsoft.FileServices.IFile>();

            tsc.SetResult(this);

            return tsc.Task;

        }

        Microsoft.FileServices.IFileFetcher Microsoft.FileServices.IFileFetcher.Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IFile, TTarget>> navigationPropertyAccessor)

        {

            return (Microsoft.FileServices.IFileFetcher) this;

        }

        public async System.Threading.Tasks.Task<Microsoft.FileServices.IFile> copy(System.String destFolderId, System.String destFolderPath, System.String newName)

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/copy");

            return (Microsoft.FileServices.IFile) Enumerable.Single<Microsoft.FileServices.File>(await this.Context.ExecuteAsync<Microsoft.FileServices.File>(requestUri, "POST", true, new OperationParameter[3]

            {

                new BodyOperationParameter("destFolderId", (object) destFolderId),

                new BodyOperationParameter("destFolderPath", (object) destFolderPath),

                new BodyOperationParameter("newName", (object) newName),

            }

            ));

        }

        public async System.Threading.Tasks.Task uploadContent(System.IO.Stream contentStream)

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/uploadContent");

            await this.Context.ExecuteAsync(requestUri, "POST", new OperationParameter[1]

            {

                new BodyOperationParameter("contentStream", (object) contentStream),

            }

            );

        }

        public async System.Threading.Tasks.Task<System.IO.Stream> content()

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/content");

            return (System.IO.Stream) Enumerable.Single<System.IO.Stream>(await this.Context.ExecuteAsync<System.IO.Stream>(requestUri, "POST", true, new OperationParameter[0]

            {

            }

            ));

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IFileFetcher:Microsoft.FileServices.IItemFetcher

    {

        System.Threading.Tasks.Task<Microsoft.FileServices.IFile> copy(System.String destFolderId, System.String destFolderPath, System.String newName);

        System.Threading.Tasks.Task uploadContent(System.IO.Stream contentStream);

        System.Threading.Tasks.Task<System.IO.Stream> content();

        global::System.Threading.Tasks.Task<Microsoft.FileServices.IFile> ExecuteAsync();

        Microsoft.FileServices.IFileFetcher Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IFile, TTarget>> navigationPropertyAccessor);

    }

    internal partial class FileFetcher:Microsoft.FileServices.ItemFetcher, Microsoft.FileServices.IFileFetcher

    {

        public FileFetcher()

        {

        }

        public async System.Threading.Tasks.Task<Microsoft.FileServices.IFile> copy(System.String destFolderId, System.String destFolderPath, System.String newName)

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/copy");

            return (Microsoft.FileServices.IFile) Enumerable.Single<Microsoft.FileServices.File>(await this.Context.ExecuteAsync<Microsoft.FileServices.File>(requestUri, "POST", true, new OperationParameter[3]

            {

                new BodyOperationParameter("destFolderId", (object) destFolderId),

                new BodyOperationParameter("destFolderPath", (object) destFolderPath),

                new BodyOperationParameter("newName", (object) newName),

            }

            ));

        }

        public async System.Threading.Tasks.Task uploadContent(System.IO.Stream contentStream)

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/uploadContent");

            await this.Context.ExecuteAsync(requestUri, "POST", new OperationParameter[1]

            {

                new BodyOperationParameter("contentStream", (object) contentStream),

            }

            );

        }

        public async System.Threading.Tasks.Task<System.IO.Stream> content()

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/content");

            return (System.IO.Stream) Enumerable.Single<System.IO.Stream>(await this.Context.ExecuteAsync<System.IO.Stream>(requestUri, "POST", true, new OperationParameter[0]

            {

            }

            ));

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFile> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IFile>(Context.CreateQuery<Microsoft.FileServices.IFile>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFile> _query;

        public async global::System.Threading.Tasks.Task<Microsoft.FileServices.IFile> ExecuteAsync()

        {

            return await EnsureQuery().ExecuteSingleAsync();

        }

        public Microsoft.FileServices.IFileFetcher Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IFile, TTarget>> navigationPropertyAccessor)

        {

            return (Microsoft.FileServices.IFileFetcher) new Microsoft.FileServices.FileFetcher()

            {

                _query = this.EnsureQuery().Expand<TTarget>(navigationPropertyAccessor)

            }

            ;

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IFileCollection:Microsoft.CoreServices.Extensions.IReadOnlyQueryableSetBase<Microsoft.FileServices.IFile>

    {

        Microsoft.FileServices.IFileFetcher GetById(System.String id);

        global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IFile>> ExecuteAsync();

        global::System.Threading.Tasks.Task AddFileAsync(Microsoft.FileServices.IFile item, System.Boolean dontSave = false);

         Microsoft.FileServices.IFileFetcher this[System.String id]

        {get;}

    }

    internal partial class FileCollection:Microsoft.CoreServices.Extensions.QueryableSet<Microsoft.FileServices.IFile>, Microsoft.FileServices.IFileCollection

    {

        internal FileCollection(global::Microsoft.OData.Client.DataServiceQuery inner,Microsoft.CoreServices.Extensions.DataServiceContextWrapper context,object entity,string path): base(inner, context, entity as Microsoft.CoreServices.Extensions.EntityBase, path)

        {

        }

        public Microsoft.FileServices.IFileFetcher GetById(System.String id)

        {

            return this[id];

        }

        public global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IFile>> ExecuteAsync()

        {

            return base.ExecuteAsyncInternal();

        }

        public global::System.Threading.Tasks.Task AddFileAsync(Microsoft.FileServices.IFile item, System.Boolean dontSave = false)

        {

            if (_entity == null)

            {

                Context.AddObject(_path, item);

            }

            else

            {

                var lastSlash = _path.LastIndexOf('/');

                var shortPath = (lastSlash >= 0 && lastSlash < _path.Length - 1) ? _path.Substring(lastSlash + 1) : _path;

                Context.AddRelatedObject(_entity, shortPath, item);

            }

            if (!dontSave)

            {

                return Context.SaveChangesAsync();

            }

            else

            {

                var retVal = new global::System.Threading.Tasks.TaskCompletionSource<object>();

                retVal.SetResult(null);

                return retVal.Task;

            }

        }

        public Microsoft.FileServices.IFileFetcher this[System.String id]

        {

            get

            {

                var query = (global::Microsoft.OData.Client.DataServiceQuery)System.Linq.Queryable.Where(Context.CreateQuery<Microsoft.FileServices.IFile>(this._path), (i) => i.id == id);

                var fetcher = new Microsoft.FileServices.FileFetcher();

                var path = query.RequestUri.ToString().Substring(Context.BaseUri.ToString().TrimEnd('/').Length + 1);

                fetcher.Initialize(Context, path);

                

                return fetcher;

            }

        }

    }

    public partial interface IFolder:Microsoft.FileServices.IItem

    {

        System.Int32 childCount
        {get;set;}

        Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IItem> children
        {get;}

        System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> copy(System.String destFolderId, System.String destFolderPath, System.String newName);

    }

    [global::Microsoft.OData.Client.Key("id")]

    public partial class Folder:Microsoft.FileServices.Item, Microsoft.FileServices.IFolder, Microsoft.FileServices.IFolderFetcher

    {

        private Microsoft.FileServices.Item _children;

        private Microsoft.FileServices.ItemCollection _childrenFetcher;

        private Microsoft.FileServices.ItemCollection _childrenCollection;

        private Microsoft.CoreServices.Extensions.EntityCollectionImpl<Microsoft.FileServices.Item> _childrenConcrete;

        private System.Int32 _childCount;

        Microsoft.FileServices.IItemCollection Microsoft.FileServices.IItemFetcher.children

        {

            get

            {

                if (this._childrenFetcher == null)

                {

                    this._childrenFetcher = new Microsoft.FileServices.ItemCollection(
                       Context != null ? Context.CreateQuery<Microsoft.FileServices.ItemFetcher>(GetPath("children")) : null,
                       Context,
                       this,
                       GetPath("children"));

                }

                

                return this._childrenFetcher;

            }

        }

        Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IItem> Microsoft.FileServices.IItem.children

        {

            get

            {

                return new Microsoft.CoreServices.Extensions.PagedCollection<Microsoft.FileServices.IItem, Microsoft.FileServices.Item>(Context, (Microsoft.CoreServices.Extensions.EntityCollectionImpl<Microsoft.FileServices.Item>) _childrenConcrete);

            }

        }

        public System.Int32 childCount

        {

            get

            {

                return _childCount;

            }

            set

            {

                if (value != _childCount)

                {

                    _childCount = value;

                    OnPropertyChanged("childCount");

                }

            }

        }

        public global::System.Collections.Generic.IList<Microsoft.FileServices.Item> children

        {

            get

            {

                if (this._childrenConcrete == null)

                {

                    this._childrenConcrete = new Microsoft.CoreServices.Extensions.EntityCollectionImpl<Microsoft.FileServices.Item>();

                    this._childrenConcrete.SetContainer(() => GetContainingEntity("children"));

                }

                

                return (global::System.Collections.Generic.IList<Microsoft.FileServices.Item>)this._childrenConcrete;

            }

            set

            {

                _childrenConcrete.Clear();

                if (value != null)

                {

                    foreach (var i in value)

                    {

                        _childrenConcrete.Add(i);

                    }

                }

            }

        }

        public Folder()

        {

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFolder> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IFolder>(Context.CreateQuery<Microsoft.FileServices.IFolder>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFolder> _query;

        global::System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> Microsoft.FileServices.IFolderFetcher.ExecuteAsync()

        {

            var tsc = new global::System.Threading.Tasks.TaskCompletionSource<Microsoft.FileServices.IFolder>();

            tsc.SetResult(this);

            return tsc.Task;

        }

        Microsoft.FileServices.IFolderFetcher Microsoft.FileServices.IFolderFetcher.Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IFolder, TTarget>> navigationPropertyAccessor)

        {

            return (Microsoft.FileServices.IFolderFetcher) this;

        }

        public async System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> copy(System.String destFolderId, System.String destFolderPath, System.String newName)

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/copy");

            return (Microsoft.FileServices.IFolder) Enumerable.Single<Microsoft.FileServices.Folder>(await this.Context.ExecuteAsync<Microsoft.FileServices.Folder>(requestUri, "POST", true, new OperationParameter[3]

            {

                new BodyOperationParameter("destFolderId", (object) destFolderId),

                new BodyOperationParameter("destFolderPath", (object) destFolderPath),

                new BodyOperationParameter("newName", (object) newName),

            }

            ));

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IFolderFetcher:Microsoft.FileServices.IItemFetcher

    {

        Microsoft.FileServices.IItemCollection children
        {get;}

        System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> copy(System.String destFolderId, System.String destFolderPath, System.String newName);

        global::System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> ExecuteAsync();

        Microsoft.FileServices.IFolderFetcher Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IFolder, TTarget>> navigationPropertyAccessor);

    }

    internal partial class FolderFetcher:Microsoft.FileServices.ItemFetcher, Microsoft.FileServices.IFolderFetcher

    {

        private Microsoft.FileServices.ItemCollection _childrenFetcher;

        private Microsoft.FileServices.ItemCollection _childrenCollection;

        private Microsoft.CoreServices.Extensions.EntityCollectionImpl<Microsoft.FileServices.Item> _childrenConcrete;

        public Microsoft.FileServices.IItemCollection children

        {

            get

            {

                if (this._childrenCollection == null)

                {

                    this._childrenCollection = new Microsoft.FileServices.ItemCollection(
                       Context != null ? Context.CreateQuery<Microsoft.FileServices.ItemFetcher>(GetPath("children")) : null,
                       Context,
                       this,
                       GetPath("children"));

                }

                

                return this._childrenCollection;

            }

        }

        public FolderFetcher()

        {

        }

        public async System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> copy(System.String destFolderId, System.String destFolderPath, System.String newName)

        {

            if (this.Context == null)

                throw new InvalidOperationException("Not Initialized");

            Uri myUri = this.GetUrl();

            if (myUri == (Uri) null)

             throw new Exception("cannot find entity");

            Uri requestUri = new Uri(myUri.ToString().TrimEnd('/') + "/copy");

            return (Microsoft.FileServices.IFolder) Enumerable.Single<Microsoft.FileServices.Folder>(await this.Context.ExecuteAsync<Microsoft.FileServices.Folder>(requestUri, "POST", true, new OperationParameter[3]

            {

                new BodyOperationParameter("destFolderId", (object) destFolderId),

                new BodyOperationParameter("destFolderPath", (object) destFolderPath),

                new BodyOperationParameter("newName", (object) newName),

            }

            ));

        }

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFolder> EnsureQuery()

        {

            if (this._query == null)

            {

                this._query = new Microsoft.CoreServices.Extensions.ReadOnlyQueryableSet<Microsoft.FileServices.IFolder>(Context.CreateQuery<Microsoft.FileServices.IFolder>(GetPath(null)), Context);

            }

            return this._query;

        }

        

        private Microsoft.CoreServices.Extensions.IReadOnlyQueryableSet<Microsoft.FileServices.IFolder> _query;

        public async global::System.Threading.Tasks.Task<Microsoft.FileServices.IFolder> ExecuteAsync()

        {

            return await EnsureQuery().ExecuteSingleAsync();

        }

        public Microsoft.FileServices.IFolderFetcher Expand<TTarget>(System.Linq.Expressions.Expression<System.Func<Microsoft.FileServices.IFolder, TTarget>> navigationPropertyAccessor)

        {

            return (Microsoft.FileServices.IFolderFetcher) new Microsoft.FileServices.FolderFetcher()

            {

                _query = this.EnsureQuery().Expand<TTarget>(navigationPropertyAccessor)

            }

            ;

        }

    }

    [Microsoft.CoreServices.Extensions.LowerCaseProperty]

    public partial interface IFolderCollection:Microsoft.CoreServices.Extensions.IReadOnlyQueryableSetBase<Microsoft.FileServices.IFolder>

    {

        Microsoft.FileServices.IFolderFetcher GetById(System.String id);

        global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IFolder>> ExecuteAsync();

        global::System.Threading.Tasks.Task AddFolderAsync(Microsoft.FileServices.IFolder item, System.Boolean dontSave = false);

         Microsoft.FileServices.IFolderFetcher this[System.String id]

        {get;}

    }

    internal partial class FolderCollection:Microsoft.CoreServices.Extensions.QueryableSet<Microsoft.FileServices.IFolder>, Microsoft.FileServices.IFolderCollection

    {

        internal FolderCollection(global::Microsoft.OData.Client.DataServiceQuery inner,Microsoft.CoreServices.Extensions.DataServiceContextWrapper context,object entity,string path): base(inner, context, entity as Microsoft.CoreServices.Extensions.EntityBase, path)

        {

        }

        public Microsoft.FileServices.IFolderFetcher GetById(System.String id)

        {

            return this[id];

        }

        public global::System.Threading.Tasks.Task<Microsoft.CoreServices.Extensions.IPagedCollection<Microsoft.FileServices.IFolder>> ExecuteAsync()

        {

            return base.ExecuteAsyncInternal();

        }

        public global::System.Threading.Tasks.Task AddFolderAsync(Microsoft.FileServices.IFolder item, System.Boolean dontSave = false)

        {

            if (_entity == null)

            {

                Context.AddObject(_path, item);

            }

            else

            {

                var lastSlash = _path.LastIndexOf('/');

                var shortPath = (lastSlash >= 0 && lastSlash < _path.Length - 1) ? _path.Substring(lastSlash + 1) : _path;

                Context.AddRelatedObject(_entity, shortPath, item);

            }

            if (!dontSave)

            {

                return Context.SaveChangesAsync();

            }

            else

            {

                var retVal = new global::System.Threading.Tasks.TaskCompletionSource<object>();

                retVal.SetResult(null);

                return retVal.Task;

            }

        }

        public Microsoft.FileServices.IFolderFetcher this[System.String id]

        {

            get

            {

                var query = (global::Microsoft.OData.Client.DataServiceQuery)System.Linq.Queryable.Where(Context.CreateQuery<Microsoft.FileServices.IFolder>(this._path), (i) => i.id == id);

                var fetcher = new Microsoft.FileServices.FolderFetcher();

                var path = query.RequestUri.ToString().Substring(Context.BaseUri.ToString().TrimEnd('/').Length + 1);

                fetcher.Initialize(Context, path);

                

                return fetcher;

            }

        }

    }

}

