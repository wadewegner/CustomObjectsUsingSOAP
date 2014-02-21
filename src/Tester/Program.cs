using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using Tester.Models;
using Enterprise = Tester.Models.Enterprise;
using Partner = Tester.Models.Partner;

namespace Tester
{
    internal class Program
    {
        private static void Main()
        {
            AsyncMain().Wait();
        }

        static async Task AsyncMain()
        {
            const string userName = "you@yourdomain.com";
            const string password = "password";
            const string orgId = "organizationId";
            const string customObject = "MyCustomObject";
            const string fieldName = "MyCustomField";

            var loginResultEnterprise = await Login<Enterprise.LoginResult>(userName, password, orgId);
            Console.WriteLine(loginResultEnterprise.UserId);

            var loginResultPartner = await Login<Partner.LoginResult>(userName, password, orgId);
            Console.WriteLine(loginResultEnterprise.UserId);

            var createObjectResult = await CreateCustomObject(customObject, loginResultPartner.SessionId, loginResultPartner.MetadataServerUrl);
            Console.WriteLine(createObjectResult.Id);

            var createFieldResult = await CreateCustomField(customObject, fieldName, loginResultPartner.SessionId, loginResultPartner.MetadataServerUrl);
            Console.WriteLine(createFieldResult.Id);
        }

        private static async Task<CreateResult> CreateCustomField(string customObject, string fieldName, string sessionId, string metadataServerUrl)
        {
            var customFieldQuery = string.Format(
@"<metadata xsi:type=""CustomField"" xmlns:cmd=""http://soap.sforce.com/2006/04/metadata"">
	<fullName>{0}.{1}__c</fullName>
	<label>{1}</label>
	<length>100</length>
    <type>Text</type>
</metadata>", customObject + "__c", fieldName); // TODO: pass this in for flexibility

            var customFieldResponse = await Create(customFieldQuery, sessionId, metadataServerUrl);

            var resultXml = new XmlDocument();
            var mgr = new XmlNamespaceManager(resultXml.NameTable);

            mgr.AddNamespace("soapenv", "http://schemas.xmlsoap.org/soap/envelope/");
            mgr.AddNamespace("meta", "http://soap.sforce.com/2006/04/metadata");

            resultXml.LoadXml(customFieldResponse);

            var selectSingleNode = resultXml.SelectSingleNode("//meta:createResponse", mgr);
            if (selectSingleNode == null) return null;

            var loginResultNode = selectSingleNode.InnerXml;
            var serializer = new XmlSerializer(typeof(CreateResult));
            var stringReader = new StringReader(loginResultNode);
            var createResult = (CreateResult)serializer.Deserialize(stringReader);

            stringReader.Dispose();

            return createResult;
        }

        private static async Task<CreateResult> CreateCustomObject(string customObject, string sessionId, string metadataServerUrl)
        {
            var customObjectQuery = string.Format(
@"<metadata xsi:type=""CustomObject"" xmlns:cmd=""http://soap.sforce.com/2006/04/metadata"">
	<fullName>{0}__c</fullName>
	<label>{0}</label>
	<pluralLabel>{0}</pluralLabel>
	<deploymentStatus>Deployed</deploymentStatus>
	<sharingModel>ReadWrite</sharingModel>
	<nameField>
		<label>ID</label>
		<type>AutoNumber</type>
	</nameField>
</metadata>", customObject);

            var customObjectResponse = await Create(customObjectQuery, sessionId, metadataServerUrl);

            var resultXml = new XmlDocument();
            var mgr = new XmlNamespaceManager(resultXml.NameTable);

            mgr.AddNamespace("soapenv", "http://schemas.xmlsoap.org/soap/envelope/");
            mgr.AddNamespace("meta", "http://soap.sforce.com/2006/04/metadata");

            resultXml.LoadXml(customObjectResponse);

            var selectSingleNode = resultXml.SelectSingleNode("//meta:createResponse", mgr);
            if (selectSingleNode == null) return null;

            var loginResultNode = selectSingleNode.InnerXml;
            var serializer = new XmlSerializer(typeof (CreateResult));
            var stringReader = new StringReader(loginResultNode);
            var createResult = (CreateResult) serializer.Deserialize(stringReader);

            stringReader.Dispose();

            return createResult;
        }

        private static async Task<string> Create(string query, string sessionId, string metadataServerUrl)
        {
            var wsdlNamespace = "http://soap.sforce.com/2006/04/metadata";
            var header = "";
            var action = "create";

            var soap = string.Format(
@"<soapenv:Envelope xmlns:xsd=""http://www.w3.org/2001/XMLSchema"" 
    xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"" 
    xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"" 
    xmlns:cmd=""{0}"" 
    xmlns:apex=""http://soap.sforce.com/2006/08/apex"">
	<soapenv:Header>
		<cmd:SessionHeader>
			<cmd:sessionId>{1}</cmd:sessionId>
		</cmd:SessionHeader>
		{2}
	</soapenv:Header>
	<soapenv:Body>
		<{3} xmlns=""{4}"">
			{5}
		</{6}>
	</soapenv:Body>
</soapenv:Envelope>", wsdlNamespace, sessionId, header, action, wsdlNamespace, query, action);

            var content = new StringContent(soap, Encoding.UTF8, "text/xml");

            using (var httpClient = new HttpClient())
            {
                var request = new HttpRequestMessage();

                request.RequestUri = new Uri(metadataServerUrl);
                request.Method = HttpMethod.Post;
                request.Content = content;

                request.Headers.Add("SOAPAction", action);

                var responseMessage = await httpClient.SendAsync(request);
                var response = await responseMessage.Content.ReadAsStringAsync();

                if (responseMessage.IsSuccessStatusCode)
                {
                    return response;
                }
            }

            throw new Exception("Failed create object");
        }

        static async Task<T> Login<T>(string userName, string password, string orgId)
        {
            string url;
            string soap;
            string wsdlType;

            if (typeof(T) == typeof(Enterprise.LoginResult))
            {
                url = "https://login.salesforce.com/services/Soap/c/29.0/" + orgId;
                soap = string.Format(
@"<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
    <soapenv:Body>
        <login xmlns=""urn:enterprise.soap.sforce.com"">
            <username>{0}</username>
            <password>{1}</password>
        </login>
    </soapenv:Body>
</soapenv:Envelope>", userName, password);
                wsdlType = "enterprise";
            }
            else
            {
                url = "https://login.salesforce.com/services/Soap/u/29.0/" + orgId;
                soap = string.Format(@"
<soapenv:Envelope xmlns:soapenv=""http://schemas.xmlsoap.org/soap/envelope/"">
    <soapenv:Body>
        <login xmlns=""urn:partner.soap.sforce.com"">
            <username>{0}</username>
            <password>{1}</password>
        </login>
    </soapenv:Body>
</soapenv:Envelope>", userName, password);
                wsdlType = "partner";
            }

            var content = new StringContent(soap, Encoding.UTF8, "text/xml");

            using (var httpClient = new HttpClient())
            {
                var request = new HttpRequestMessage();

                request.RequestUri = new Uri(url);
                request.Method = HttpMethod.Post;
                request.Content = content;

                request.Headers.Add("SOAPAction", "login");

                var responseMessage = await httpClient.SendAsync(request);
                var response = await responseMessage.Content.ReadAsStringAsync();

                if (responseMessage.IsSuccessStatusCode)
                {
                    var resultXml = new XmlDocument();
                    var mgr = new XmlNamespaceManager(resultXml.NameTable);

                    mgr.AddNamespace("soapenv", "http://schemas.xmlsoap.org/soap/envelope/");
                    mgr.AddNamespace("ns", string.Format("urn:{0}.soap.sforce.com", wsdlType));

                    resultXml.LoadXml(response);

                    var loginResultNode = resultXml.SelectSingleNode("//ns:loginResponse", mgr).InnerXml;

                    var serializer = new XmlSerializer(typeof(T));
                    var stringReader = new StringReader(loginResultNode);

                    var loginResult = (T)serializer.Deserialize(stringReader);
                    stringReader.Dispose();

                    return loginResult;
                }

                throw new Exception("Failed login");
            }
        }
    }
}
