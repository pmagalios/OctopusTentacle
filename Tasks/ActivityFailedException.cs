using System;
using System.Runtime.Serialization;

namespace Octopus.Shared.Tasks
{
    public class ActivityFailedException : Exception
    {
        public ActivityFailedException(string message) : base(message)
        {
        }

        public ActivityFailedException(SerializationInfo info, StreamingContext context) : base(info, context)
        {
        }
    }
}