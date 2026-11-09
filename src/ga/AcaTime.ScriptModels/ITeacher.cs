namespace AcaTime.ScriptModels
{
    /// <summary>
    /// Інтерфейс для представлення викладача з властивостями тільки для читання.
    /// </summary>
    public interface ITeacher
    {
        /// <summary>Ідентифікатор викладача.</summary>
        long Id { get; }

        /// <summary>Ім'я викладача.</summary>
        string Name { get; }

        /// <summary>Посада викладача.</summary>
        string Position { get; }
    }
}