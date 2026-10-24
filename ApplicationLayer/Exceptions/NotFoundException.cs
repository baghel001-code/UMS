namespace ApplicationLayer.Exceptions
{
    public class NotFoundException : AppException
    {
        public NotFoundException(string entity, object id) : base($"{entity} '{id}' was not found.") { }
    }
}
