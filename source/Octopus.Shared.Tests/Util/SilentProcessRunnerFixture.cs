﻿using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Principal;
using System.Text;
using System.Threading;
using FluentAssertions;
using NUnit.Framework;
using Octopus.Shared.Util;

namespace Octopus.Shared.Tests.Util
{
    [TestFixture]
    public class SilentProcessRunnerFixture
    {
        [Test]
        public void ExitCode_ShouldBeReturned()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var command = "cmd.exe";
                var arguments = @"/c exit 9999";
                var workingDirectory = "";
                var networkCredential = default(NetworkCredential);
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(9999, "our custom exit code should be reflected");
                debugMessages.ToString().Should().ContainEquivalentOf($"Starting {command} in  as {WindowsIdentity.GetCurrent().Name}");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
                infoMessages.ToString().Should().BeEmpty("no messages should be written to stdout");
            }
        }

        [Test]
        public void DebugLogging_ShouldContainDiagnosticsInfo_ForDefault()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var command = "cmd.exe";
                var arguments = @"/c echo hello";
                var workingDirectory = "";
                var networkCredential = default(NetworkCredential);
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                debugMessages.ToString().Should().ContainEquivalentOf(command, "the command should be logged")
                    .And.ContainEquivalentOf(WindowsIdentity.GetCurrent().Name, "the current user details should be logged");
                infoMessages.ToString().Should().ContainEquivalentOf("hello");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            }
        }

        [Test]
        public void DebugLogging_ShouldContainDiagnosticsInfo_DifferentUser()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var user = new TransientUserPrincipal())
            {
                var command = "cmd.exe";
                var arguments = @"/c echo %userdomain%\%username%";
                // Target the CommonApplicationData folder since this is a place the particular user can get to
                var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var networkCredential = user.GetCredential();
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                debugMessages.ToString().Should().ContainEquivalentOf(command, "the command should be logged")
                    .And.ContainEquivalentOf($@"{user.DomainName}\{user.UserName}", "the custom user details should be logged")
                    .And.ContainEquivalentOf(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "the working directory should be logged");
                infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            }
        }

        [Test]
        public void RunningAsDifferentUser_ShouldCopySpecialEnvironmentVariables()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var user = new TransientUserPrincipal())
            {
                var command = "cmd.exe";
                var arguments = @"/c echo %customenvironmentvariable%";
                // Target the CommonApplicationData folder since this is a place the particular user can get to
                var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var networkCredential = user.GetCredential();
                var customEnvironmentVariables = new Dictionary<string, string>
                {
                    {"customenvironmentvariable", "customvalue"}
                };

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                infoMessages.ToString().Should().ContainEquivalentOf("customvalue", "the environment variable should have been copied to the child process");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            }
        }
        
        [Test]
        public void RunningAsDifferentUser_CanWriteToItsOwnTempPath()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var user = new TransientUserPrincipal())
            {
                var command = "cmd.exe";
                var arguments = @"/c echo hello > %temp%hello.txt";
                // Target the CommonApplicationData folder since this is a place the particular user can get to
                var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var networkCredential = user.GetCredential();
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion after writing to the temp folder for the other user");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            }
        }

        [Test]
        public void CancellationToken_ShouldForceKillTheProcess()
        {
            // Terminate the process after a very short time so the test doesn't run forever
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            {
                // Starting a new instance of cmd.exe will run indefinitely waiting for user input
                var command = "cmd.exe";
                var arguments = "";
                var workingDirectory = "";
                var networkCredential = default(NetworkCredential);
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().BeLessOrEqualTo(0, "the process should have been terminated");
                infoMessages.ToString().Should().ContainEquivalentOf("Microsoft Windows", "the default command-line header would be written to stdout");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
            }
        }

        [Test]
        public void EchoHello_ShouldWriteToStdOut()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var command = "cmd.exe";
                var arguments = @"/c echo hello";
                var workingDirectory = "";
                var networkCredential = default(NetworkCredential);
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
                infoMessages.ToString().Should().ContainEquivalentOf("hello");
            }
        }

        [Test]
        public void EchoError_ShouldWriteToStdErr()
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var command = "cmd.exe";
                var arguments = @"/c echo Something went wrong! 1>&2";
                var workingDirectory = "";
                var networkCredential = default(NetworkCredential);
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                infoMessages.ToString().Should().BeEmpty("no messages should be written to stdout");
                errorMessages.ToString().Should().ContainEquivalentOf("Something went wrong!");
            }
        }

        [Test]
        [TestCase("cmd.exe", @"/c echo %userdomain%\%username%")]
        [TestCase("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
        public void RunAsCurrentUser_ShouldWork(string command, string arguments)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                var workingDirectory = "";
                var networkCredential = default(NetworkCredential);
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
                infoMessages.ToString().Should().ContainEquivalentOf($@"{Environment.UserDomainName}\{Environment.UserName}");
            }
        }

        [Test]
        [TestCase("cmd.exe", @"/c echo %userdomain%\%username%")]
        [TestCase("powershell.exe", "-command \"Write-Host $env:userdomain\\$env:username\"")]
        public void RunAsDifferentUser_ShouldWork(string command, string arguments)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var user = new TransientUserPrincipal())
            {
                // Target the CommonApplicationData folder since this is a place the particular user can get to
                var workingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
                var networkCredential = user.GetCredential();
                var customEnvironmentVariables = new Dictionary<string, string>();

                var exitCode = Execute(command, arguments, workingDirectory, out var debugMessages, out var infoMessages, out var errorMessages, networkCredential, customEnvironmentVariables, cts.Token);

                exitCode.Should().Be(0, "the process should have run to completion");
                errorMessages.ToString().Should().BeEmpty("no messages should be written to stderr");
                infoMessages.ToString().Should().ContainEquivalentOf($@"{user.DomainName}\{user.UserName}");
            }
        }

        private static int Execute(
            string command, 
            string arguments,
            string workingDirectory,
            out StringBuilder debugMessages,
            out StringBuilder infoMessages,
            out StringBuilder errorMessages,
            NetworkCredential networkCredential,
            IDictionary<string, string> customEnvironmentVariables,
            CancellationToken cancel)
        {
            var debug = new StringBuilder();
            var info = new StringBuilder();
            var error = new StringBuilder();
            var exitCode = SilentProcessRunner.ExecuteCommand(
                command,
                arguments,
                workingDirectory,
                x =>
                {
                    Console.WriteLine($"{DateTime.UtcNow} DBG: {x}");
                    debug.Append(x);
                },
                x =>
                {
                    Console.WriteLine($"{DateTime.UtcNow} INF: {x}");
                    info.Append(x);
                },
                x =>
                {
                    Console.WriteLine($"{DateTime.UtcNow} ERR: {x}");
                    error.Append(x);
                },
                networkCredential,
                customEnvironmentVariables,
                cancel);

            debugMessages = debug;
            infoMessages = info;
            errorMessages = error;

            return exitCode;
        }
    }
}