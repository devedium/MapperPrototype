using Mapper;

namespace MapperPrototype
{
    public class Person
    {
        public int Id { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }        
    }

    public class PersonDto
    {
        public int Id { get; set; }
        public string GivenName { get; set; }
        public string Surname { get; set; }
    }

    
    internal class Program
    {
        static void Main(string[] args)
        {
            var person = new Person()
            {
                Id = 1,
                FirstName = "John",
                LastName = "Smith",
            };

            var mapper = new Mapper<Person,PersonDto>();
            var mapping = mapper.Bind(s => s.FirstName, d => d.GivenName)
                  .Bind(s => s.LastName, d => d.Surname)
                  .Build();

            var personDto = mapping.Map(person);
            Console.WriteLine($"{personDto.GivenName} {personDto.Surname}");
        }
    }
}