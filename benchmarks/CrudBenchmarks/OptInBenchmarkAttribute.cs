using System;

namespace CrudBenchmarks;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class OptInBenchmarkAttribute : Attribute;
