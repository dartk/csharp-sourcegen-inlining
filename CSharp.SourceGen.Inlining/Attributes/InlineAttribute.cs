#nullable enable
namespace CSharp.SourceGen.Inlining;


[AttributeUsage(AttributeTargets.Method)]
internal class InlineAttribute : Attribute
{
}


internal static class Inline
{

    [AttributeUsage(AttributeTargets.Method)]
    internal class PrivateAttribute : Attribute
    {
        public PrivateAttribute(string? name = null)
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method)]
    internal class PublicAttribute : Attribute
    {
        public PublicAttribute(string? name = null)
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method)]
    internal class ProtectedAttribute : Attribute
    {
        public ProtectedAttribute(string? name = null)
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method)]
    internal class InternalAttribute : Attribute
    {
        public InternalAttribute(string? name = null)
        {
        }
    }

}
