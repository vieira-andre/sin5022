using System;

namespace ForTest
{
    class Program
    {
        static void Main(string[] args) { }

        static int RandomCalc(int i)
        {
            i++;
            if (i % 2 == 0)
                i++;
            return i;
        }

        static string Reversion(string str)
        {
            char[] charArray = str.ToCharArray();
            Array.Reverse(charArray);
            return new string(charArray);
        }
    }
}