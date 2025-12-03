using MediaBrowser.Controller.Collections;
using System;
using System.Linq;

class Test {
    static void Main() {
        Console.WriteLine("ICollectionManager methods:");
        var methods = typeof(ICollectionManager).GetMethods();
        foreach (var method in methods.OrderBy(m => m.Name)) {
            var paramTypes = method.GetParameters().Select(p => p.ParameterType.Name);
            Console.WriteLine("  - " + method.Name + "(" + string.Join(", ", paramTypes) + ")");
        }
    }
}
