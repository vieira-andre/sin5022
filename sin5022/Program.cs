﻿using Microsoft.CSharp;
using System;
using System.CodeDom.Compiler;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace sin5022
{
    class Program
    {
        static void Main(string[] args)
        {
            var resultPath = ConfigurationManager.AppSettings["resultpath"];

            var provider = new CSharpCodeProvider();
            var parameters = new CompilerParameters(new[] { "mscorlib.dll", "System.Core.dll" }, resultPath, true)
            {
                GenerateExecutable = true
            };

            string sourceCode = File.ReadAllText(ConfigurationManager.AppSettings["sourcecode"]);

            Tuple<bool, string> codeWithMethodInPlace = PromoteMethodPlacement(sourceCode);

            if (!codeWithMethodInPlace.Item1)
            {
                Console.WriteLine(codeWithMethodInPlace.Item2);
                Console.ReadKey();

                Environment.Exit(-1);
            }

            string codeWithMethodCallInPlace = PromoteMethodCall(codeWithMethodInPlace.Item2);
            string codeWithAssertionInPlace = PromoteAssertion(codeWithMethodCallInPlace);
            string codeToBeCompiled = PromoteCodeCoverage(codeWithAssertionInPlace);

            CompilerResults cr = provider.CompileAssemblyFromSource(parameters, codeToBeCompiled);

            if (cr.Errors.Count > 0)
            {
                Console.WriteLine("Errors building\n======\n\n{0}\n\n======\ninto\n\n{1}",
                    codeToBeCompiled, cr.PathToAssembly);

                foreach (CompilerError ce in cr.Errors)
                {
                    Console.WriteLine("  {0}", ce.ToString());
                    Console.WriteLine();
                }

                Console.ReadKey();
            }
            else
            {
                Console.WriteLine("Source\n======\n\n{0}\n\n======\nbuilt into {1} successfully.",
                    codeToBeCompiled, cr.PathToAssembly);

                Console.ReadKey();

                using (var process = Process.Start(resultPath))
                {
                    process.WaitForExit();
                }
            }
        }

        private static Tuple<bool, string> PromoteMethodPlacement(string sourceCode)
        {
            var methodName = ConfigurationManager.AppSettings["methodname"];

            MatchCollection extractedMethodsInSource = Regex.Matches(sourceCode,
                @"((?:(?:public|private|protected|static|readonly|abstract)\s+)*)\s*(\w+)\s*(\w+)\(.*?\)\s*({(?:{[^{}]*(?:{[^{}]*}|.)*?[^{}]*}|.)*?})",
                RegexOptions.Singleline);

            if (extractedMethodsInSource.Count < 0)
                return Tuple.Create(false, "[Error] No methods were matched in source code.");

            string desiredMethodFromSource = null;

            foreach (Match match in extractedMethodsInSource)
            {
                if (match.Groups[3].Value.Equals(methodName))
                    desiredMethodFromSource = match.Value;
            }

            if (!string.IsNullOrEmpty(desiredMethodFromSource))
            {
                string template = File.ReadAllText(ConfigurationManager.AppSettings["template"]);

                string instrumentedMethod = InstrumentMethod(desiredMethodFromSource);
                string sourceWithInsertedMethod = Regex.Replace(template, @"(__methodPlacement__)", instrumentedMethod);

                return Tuple.Create(true, sourceWithInsertedMethod);
            }
            else
                return Tuple.Create(false, "[Error] The specified method could not be found.");
        }

        private static string InstrumentMethod(string uninstrumentedMethod)
        {
            string tempStr = uninstrumentedMethod.Replace(Environment.NewLine, $"ncount++;");
            tempStr = Regex.Replace(tempStr, @"(if|for|foreach|while)(\s*)((\()[^)]*(\)))(ncount\+\+;)", "$1$2$3");
            int idx = tempStr.IndexOf(@"ncount++;");
            tempStr = (idx < 0) ? tempStr : tempStr.Remove(idx, @"ncount++;".Length);
            int lastIdx = tempStr.LastIndexOf(@"ncount++;");
            tempStr = (idx < 0) ? tempStr : tempStr.Remove(lastIdx, @"ncount++;".Length);

            return tempStr;
        }

        private static string PromoteMethodCall(string codeWithMethodPlaced)
        {
            var methodName = ConfigurationManager.AppSettings["methodname"];
            var requestArgs = File.ReadAllText(ConfigurationManager.AppSettings["request"]);
            var methodCall = string.Concat("var result = ", methodName, "(", requestArgs, ");");

            string codeWithMethodCallInPlace = Regex.Replace(codeWithMethodPlaced, @"(__methodCall__)", methodCall);

            return codeWithMethodCallInPlace;
        }

        private static string PromoteAssertion(string codeWithMethodCallInPlace)
        {
            var expectedValue = File.ReadAllText(ConfigurationManager.AppSettings["response"]);
            var assertionParams = File.ReadAllText(ConfigurationManager.AppSettings["assertion"]);

            string[] assertParams = assertionParams.Split(new string[] { "<breakParam>" }, StringSplitOptions.None);

            string type = assertParams[0];
            var assertionCheck = assertParams[1];

            string expectedResult = string.Concat(type + " ", "expectedValue = ", expectedValue, ";");
            string codeWithExpectedResultInplace = Regex.Replace(codeWithMethodCallInPlace, @"(__expectedResult__)", expectedResult);

            string codeWithAssertionInPlace = Regex.Replace(codeWithExpectedResultInplace, @"(__assertionCheck__)", assertionCheck);

            return codeWithAssertionInPlace;
        }

        private static string PromoteCodeCoverage(string codeWithAssertionInPlace)
        {
            int ncountTotal = Regex.Matches(codeWithAssertionInPlace, @"ncount\+\+;").Count;

            string coverage = $"(Decimal.Divide(ncount, {ncountTotal}) * 100)";
            string codeCoverageStretch = string.Concat("Console.WriteLine(", "\"Code coverage (%): {0:0.0}\", ", coverage, ");");
            string codeWithCodeCoverageInPlace = Regex.Replace(codeWithAssertionInPlace, @"(__codeCoverage__)", codeCoverageStretch);

            return codeWithCodeCoverageInPlace;
        }
    }
}
