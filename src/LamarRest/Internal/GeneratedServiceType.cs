using System;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using Baseline;
using Baseline.Reflection;
using Lamar;
using LamarCompiler;
using LamarCompiler.Model;
using LamarRest.Internal.Frames;
using Microsoft.Extensions.Configuration;

namespace LamarRest.Internal
{
    public class GeneratedServiceType
    {
        private readonly GeneratedType _generatedType;

        public static GeneratedServiceType For(Type serviceType, IContainer container)
        {
            var assembly = new GeneratedAssembly(new GenerationRules("LamarRest"));
            var generatedType = new GeneratedServiceType(assembly, serviceType);

            var services = container.CreateServiceVariableSource();
            new AssemblyGenerator().Compile(assembly, services);

            return generatedType;
        }
        
        public GeneratedServiceType(GeneratedAssembly assembly, Type serviceType)
        {
            var name = serviceType.Name.StartsWith("I")
                ? serviceType.Name.TrimStart('I')
                : serviceType.Name + "Implementation";

            _generatedType = assembly.AddType(name, serviceType);

            foreach (var methodInfo in serviceType.GetMethods().Where(x => x.HasAttribute<PathAttribute>()))
            {
                var generatedMethod = _generatedType.MethodFor(methodInfo.Name);
                BuildOut(serviceType, methodInfo, generatedMethod);
            }
        }

        public Type CompiledType => _generatedType.CompiledType;
        public string SourceCode => _generatedType.SourceCode;

public static void BuildOut(
    // The contract type
    Type interfaceType, 
    
    // The MethodInfo from Reflection that describes
    // the Url structure through attributes, the input type
    // if any, and the .Net type of the response (if any)
    MethodInfo definition, 
    
    // This models the method being generated by LamarCompiler
    GeneratedMethod generated)
{
    var path = definition.GetCustomAttribute<PathAttribute>();

    // Get the right HttpClient from IHttpClientFactory
    generated.Frames.Add(new BuildClientFrame(interfaceType));

    // Build out the Url from the method arguments and route
    // pattern
    var urlFrame = new FillUrlFrame(definition);
    generated.Frames.Add(urlFrame);

    
    // See if there is an input type that should be serialized
    // to the HTTP request body
    var inputType = DetermineRequestType(definition);
    if (inputType == null)
    {
        // Just build the HttpRequestMessage
        generated.Frames.Add(new BuildRequestFrame(path.Method, urlFrame, null));
    }
    else
    {
        // Add a step to serialize the input model to Json
        generated.Frames.Add(new SerializeJsonFrame(inputType));
        
        // Just build the HttpRequestMessage
        generated.Frames.Add(new BuildRequestFrame(path.Method, urlFrame, new SerializeJsonFrame(inputType)));
    }

    // Actually call the HttpClient to send the request
    generated.Frames.Call<HttpClient>(x => x.SendAsync(null));

    // Is there a response type that should be serialized out of the HTTP
    // response body?
    var returnType = DetermineResponseType(definition);
    if (returnType != null)
    {
        // Deserialize the response JSON into a new variable
        var deserialize = new DeserializeObjectFrame(returnType);
        generated.Frames.Add(deserialize);
        
        // Return that deserialized object from the method
        generated.Frames.Return(deserialize.ReturnValue);
    }
}

        public static Type DetermineResponseType(MethodInfo definition)
        {
            if (definition.ReturnType == typeof(void)) return null;
            if (definition.ReturnType == typeof(Task)) return null;

            if (definition.ReturnType.Closes(typeof(Task<>)))
            {
                return definition.ReturnType.GetGenericArguments().Single();
            }

            return null;
        }

        public static Type DetermineRequestType(MethodInfo definition)
        {
            var parameters = definition.GetParameters();
            var path = definition.GetAttribute<PathAttribute>();
            var segments = path.Path.TrimStart('/').Split('/');

            var first = definition.GetParameters().FirstOrDefault();
            if (first == null) return null;

            var segmentName = $"{{{first.Name}}}";
            if (segments.Contains(segmentName)) return null;

            return first.ParameterType;
        }
    }
}