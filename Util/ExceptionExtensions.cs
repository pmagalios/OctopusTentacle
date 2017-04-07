﻿using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Octopus.Shared.Util
{
    public static class ExceptionExtensions
    {
        public static string PrettyPrint(this Exception ex, bool printStackTrace = true)
        {
            var sb = new StringBuilder();
            PrettyPrint(sb, ex, printStackTrace);
            return sb.ToString().Trim();
        }

        static void PrettyPrint(StringBuilder sb, Exception ex, bool printStackTrace)
        {
            var aex = ex as AggregateException;
            if (aex != null)
            {
                AppendAggregateException(sb, printStackTrace, aex);
                return;
            }

            if(ex is OperationCanceledException)
            {
                sb.AppendLine("The task was canceled");
                return;
            }

            var sqlEx = ex as SqlException;
            if (sqlEx != null)
            {
                sb.AppendLine($"SQL Error {sqlEx.Number} - {ex.Message}");
            }
            else
            {
                sb.AppendLine(ex.Message);
            }


            if (ex is ControlledFailureException)
                return;

            if (printStackTrace)
            {
                AddStackTrace(sb, ex);
            }

            if (ex.InnerException == null)
                return;

            if (printStackTrace)
                sb.AppendLine("--Inner Exception--");

            PrettyPrint(sb, ex.InnerException, printStackTrace);
        }

        static void AppendAggregateException(StringBuilder sb, bool printStackTrace, AggregateException aex)
        {
            if (!printStackTrace && aex.InnerExceptions.Count == 1)
            {
                PrettyPrint(sb, aex.InnerException, printStackTrace);
            }
            else
            {
                sb.AppendLine("Aggregate Exception");
                if(printStackTrace)
                    AddStackTrace(sb, aex);
                for (var x = 0; x < aex.InnerExceptions.Count; x++)
                {
                    sb.AppendLine($"--Inner Exception {x+1}--");
                    PrettyPrint(sb, aex.InnerExceptions[x], printStackTrace);
                }
            }
        }

        static void AddStackTrace(StringBuilder sb, Exception ex)
        {
            var rtle = ex as ReflectionTypeLoadException;
            if (rtle != null)
                AddReflectionTypeLoadExceptionDetails(rtle, sb);

            sb.AppendLine(ex.GetType().FullName);
            try
            {
                sb.AppendLine(ex.StackTraceEx()); // Sometimes fails printing the trace
            }
            catch
            {
                sb.AppendLine(ex.StackTrace);
            }
        }

        static void AddReflectionTypeLoadExceptionDetails(ReflectionTypeLoadException rtle, StringBuilder sb)
        {
            foreach (var loaderException in rtle.LoaderExceptions)
            {
                sb.AppendLine();
                sb.AppendLine("--Loader Exception--");
                PrettyPrint(sb, loaderException, true);

                var fusionLog = (loaderException as FileNotFoundException)?.FusionLog;
                if (!string.IsNullOrEmpty(fusionLog))
                    sb.Append("Fusion log: ").AppendLine(fusionLog);
            }
        }

        public static Exception UnpackFromContainers(this Exception error)
        {
            var aggregateException = error as AggregateException;
            if (aggregateException != null && aggregateException.InnerExceptions.Count == 1)
            {
                return UnpackFromContainers(aggregateException.InnerExceptions[0]);
            }

            if (error is TargetInvocationException && error.InnerException != null)
            {
                return UnpackFromContainers(error.InnerException);
            }

            return error;
        }

        public static string SuggestUrlReservations(IList<Uri> prefixes)
        {
            var message = new StringBuilder();
            message.Append("The HTTP server could not start because namespace reservations have not been made. ");
            message.AppendLine("Ensure that the current user has access to listen on these prefixes by running the following command(s):");
            foreach (var prefix in prefixes)
            {
                message.AppendFormat("  netsh http add urlacl url={0}://+:{1}{2} user={3}\\{4}",
                    prefix.Scheme,
                    prefix.Port,
                    prefix.PathAndQuery,
                    Environment.UserDomainName,
                    Environment.UserName)
                    .AppendLine();
            }

            return message.ToString();
        }

        public static string SuggestSolution(this HttpListenerException error, IList<Uri> prefixes)
        {
            if (error.ErrorCode != 5)
                return null;

            var message = new StringBuilder();
            message.AppendFormat("The service was unable to start because the HttpListener returned an error: {0}", error.Message).AppendLine();
            message.AppendLine("This could occur for one of two reasons. First, ensure that the current user has access to listen on these prefixes by running the following command(s):");
            foreach (var prefix in prefixes)
            {
                message.AppendFormat("  netsh http add urlacl url={0}://+:{1}{2} user={3}\\{4}",
                    prefix.Scheme,
                    prefix.Port,
                    prefix.PathAndQuery,
                    Environment.UserDomainName,
                    Environment.UserName)
                    .AppendLine();
            }

            message.Append("Alternatively, this error might have been caused by one of the ports being in use by another process. Ensure that no other processes are listening on the TCP port(s): ")
                .Append(string.Join(", ", prefixes.Select(p => p.Port)));

            return message.ToString();
        }
        
    }
}