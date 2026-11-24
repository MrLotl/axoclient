namespace AxoClient.Launch.Contract
{
    public interface IJavaArgumentsHelper
    {
        /// <summary>Enthalten die Argumente -Xmx/-Xms (der RAM-Regler hat Vorrang)?</summary>
        bool HasMemoryArgument(string arguments);

        string StripMemory(string arguments);

        /// <summary>Jedes Argument muss mit - beginnen, Anführungszeichen müssen paarig sein, keine Steuerzeichen.</summary>
        bool IsValid(string arguments);

        List<string> Split(string arguments);
    }
}
