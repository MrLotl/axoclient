namespace AxoClient.Launch.Contract
{
    public interface IJavaArgumentsHelper
    {
        /// <summary>Enthalten die Argumente -Xmx/-Xms (der RAM-Regler hat Vorrang)?</summary>
        bool HasMemoryArg(string args);

        string StripMemory(string args);

        /// <summary>Jedes Argument muss mit - beginnen, Anführungszeichen müssen paarig sein, keine Steuerzeichen.</summary>
        bool IsValid(string args);

        List<string> Split(string args);
    }
}
