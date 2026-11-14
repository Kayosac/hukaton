namespace Hakaton.Expeditions
{
    /// <summary>
    /// Тело, которое карта может отвлечь (Вороньё): пока отвлечено, оно ни на кого не нападает — не бьёт, не стреляет,
    /// не кусает; может метаться, отмахиваться на месте или убегать. Отвлекает источник (стая ворон): продлевает
    /// <see cref="Distract"/> каждый свой шаг и снимает <see cref="EndDistraction"/>, когда уходит. Заморозка и
    /// оглушение важнее: они действуют как обычно.
    /// </summary>
    public interface IDistractable
    {
        /// <summary>Отвлечено ещё <paramref name="seconds"/> с (не короче уже идущего). False — нельзя (мёртвое).</summary>
        bool Distract(float seconds);
        /// <summary>Снимает отвлечение сразу: тело возвращается к обычному поведению.</summary>
        void EndDistraction();
        /// <summary>Сейчас отвлечено и не нападает.</summary>
        bool Distracted { get; }
    }
}
