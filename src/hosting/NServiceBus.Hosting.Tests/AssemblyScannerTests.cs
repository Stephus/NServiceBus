﻿namespace NServiceBus.Hosting.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Reflection;
    using Helpers;
    using NUnit.Framework;

    [TestFixture]
    public class AssemblyScannerTests
    {
        static readonly string TestDllDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestDlls");

        static readonly string[] NotProperDotNetDlls =
            new[]
                {
                    "libzmq-v120-mt-3_2_3.dll",
                    "Tail.exe",
                    "some_random.dll",
                    "some_random.exe",
                };

        static readonly string[] DoesNotReferenceNServiceBus = new[] {"Rebus.dll"};

        static readonly string[] DoesInFactContainMessageHandlers = new[] { "NServiceBus.Core.Tests" }; //< assembly name, not file name

        [TestFixture]
        public class When_told_to_scan_app_domain
        {
            AssemblyScannerResults results;

            [SetUp]
            public void Context()
            {
                var loadThisIntoAppDomain = new SomeHandlerThatEnsuresThatWeKeepReferencingNsbCore();

                var someDir = Path.Combine(Path.GetTempPath(), "empty");
                Directory.CreateDirectory(someDir);

                results = new AssemblyScanner(someDir)
                    .IncludeAppDomainAssemblies()
                    .GetScannableAssemblies();
            }

            class SomeHandlerThatEnsuresThatWeKeepReferencingNsbCore : IHandleMessages<string>
            {
                public void Handle(string message)
                {
                }
            }

            [Test]
            public void Should_use_AppDomain_Assemblies_if_flagged()
            {
                var collection = results.Assemblies.Select(a => a.GetName().Name).ToArray();
             
                CollectionAssert.Contains(collection, "NServiceBus.Core.Tests");
            }
        }

        [TestFixture]
        public class When_inclusion_predicate_is_used
        {
            AssemblyScannerResults results;
            List<SkippedFile> skippedFiles;

            [SetUp]
            public void Context()
            {
                results = new AssemblyScanner(TestDllDirectory)
                    .IncludeAssemblies(new[] {"NServiceBus.Core.Tests.dll"})
                    .GetScannableAssemblies();

                skippedFiles = results.SkippedFiles;
            }

            [Test]
            public void only_files_explicitly_included_are_returned()
            {
                Assert.That(results.Assemblies, Has.Count.EqualTo(1));
                Assert.That(results.Errors, Has.Count.EqualTo(0));
                Assert.That(skippedFiles, Has.Count.GreaterThan(0));

                Assert.That(results.Assemblies.Single().GetName().Name, Is.EqualTo("NServiceBus.Core.Tests"));
            }
        }

        [TestFixture]
        public class When_exclusion_predicate_is_used
        {
            AssemblyScannerResults results;
            List<SkippedFile> skippedFiles;

            [SetUp]
            public void Context()
            {
                results = new AssemblyScanner(TestDllDirectory)
                .SkipAssemblies(new[] { "Rebus.dll" })
                    .GetScannableAssemblies();

                skippedFiles = results.SkippedFiles;
            }

            [Test]
            public void no_files_explicitly_excluded_are_returned()
            {
                var explicitlySkippedDll = skippedFiles.FirstOrDefault(s => s.FilePath.Contains("Rebus.dll"));

                Assert.That(explicitlySkippedDll, Is.Not.Null);
                Assert.That(explicitlySkippedDll.SkipReason, Contains.Substring("Explicitly excluded from scanning"));
            }
        }

        [TestFixture]
        public class When_directory_is_scanned
        {
            AssemblyScannerResults results;
            List<SkippedFile> skippedFiles;

            [SetUp]
            public void Context()
            {
                results = new AssemblyScanner(TestDllDirectory)
                    .GetScannableAssemblies();

                skippedFiles = results.SkippedFiles;
            }

            [Test]
            public void non_dotnet_files_are_skipped()
            {
                foreach (var notProperDll in NotProperDotNetDlls)
                {
                    var skippedFile = skippedFiles.FirstOrDefault(f => f.FilePath.Contains(notProperDll));
                    
                    if (skippedFile == null)
                        throw new AssertionException(string.Format("Could not find skipped file matching {0}",
                                                                   notProperDll));
                    
                    Assert.That(skippedFile.SkipReason, Contains.Substring("not a .NET assembly"));
                }
            }

            [Test]
            public void assemblies_without_nsb_reference_are_skipped()
            {
                foreach (var cannotContainMessageHandler in DoesNotReferenceNServiceBus)
                {
                    var skippedFile = skippedFiles.FirstOrDefault(f => f.FilePath.Contains(cannotContainMessageHandler));
                    
                    if (skippedFile == null)
                        throw new AssertionException(string.Format("Could not find skipped file matching {0}",
                                                                   cannotContainMessageHandler));
                    Assert.That(skippedFile.SkipReason,
                                Contains.Substring("Does not reference NServiceBus and thus cannot contain any handlers"));
                }
            }

            [Test]
            public void dll_with_message_handlers_gets_loaded()
            {
                Assert.That(results.Assemblies, Has.Count.EqualTo(1));
                Assert.That(results.Errors, Has.Count.EqualTo(0));

                foreach (var containsHandlers in DoesInFactContainMessageHandlers)
                {
                    var assembly = results.Assemblies
                                          .FirstOrDefault(a => a.GetName().Name.Contains(containsHandlers));

                    if (assembly == null)
                        throw new AssertionException(string.Format("Could not find loaded assembly matching {0}",
                                                                   containsHandlers));
                }
            }
        }
    }
}