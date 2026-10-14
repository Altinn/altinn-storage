#nullable disable

using System;
using System.Collections.Generic;

namespace Altinn.Platform.Storage.Models;

/// <summary>
/// View for signature data
/// </summary>
public class SignatureView
{
    /// <summary>
    /// Gets or sets The unique identifier for Signature.
    /// </summary>
    public int SignatureID { get; set; }

    /// <summary>
    /// Gets or sets The party id for the person who has signed.
    /// </summary>
    public int SignedByUser { get; set; }

    /// <summary>
    /// Gets or sets The user ssn/org number for the signee.
    /// </summary>
    public string SignedByUserSSN { get; set; }

    /// <summary>
    /// Gets or sets The user name for the person who has signed.
    /// </summary>
    public string SignedByUserName { get; set; }

    /// <summary>
    /// Gets or sets The date and time at which the signature was created.
    /// </summary>
    public DateTime CreatedDateTime { get; set; }

    /// <summary>
    /// Gets or sets The signature stored in binary format.
    /// </summary>
    public byte[] Signature { get; set; }

    /// <summary>
    /// Gets or sets The text for that signature.
    /// </summary>
    public string SignatureText { get; set; }

    /// <summary>
    /// Gets or sets whether this signing is done for all the items in  the form
    /// 1=Group signing is set, 2=Group signing not set, 3=Group signing defined at each form level
    /// </summary>
    public int IsSigningAllRequired { get; set; }

    /// <summary>
    /// Gets or sets The authentication level attached with that signature.
    /// </summary>
    public int AuthenticationLevelID { get; set; }

    /// <summary>
    /// Gets or sets A Key that represents what level the user has when the entry was made
    /// </summary>
    public int AuthenticationMethod { get; set; }

    /// <summary>
    /// Gets or sets The name by which certificate was issued.
    /// </summary>
    public string CertificateIssuedByName { get; set; }

    /// <summary>
    /// Gets or sets The name for whom the signature was issued.
    /// </summary>
    public string CertificateIssuedForName { get; set; }

    /// <summary>
    /// Gets or sets Date and time from which the signature will be valid.
    /// </summary>
    public DateTime CertificateValidFrom { get; set; }

    /// <summary>
    /// Gets or sets Date and time till which the signature will be valid.
    /// </summary>
    public DateTime CertificateValidTo { get; set; }

    /// <summary>
    /// Gets or sets List of signed attachments.
    /// </summary>
    public List<int> SignedAttachmentList { get; set; }

    /// <summary>
    /// Gets or sets List of signed forms.
    /// </summary>
    public List<int> SignedFromList { get; set; }

    /// <summary>
    /// Gets or sets Step for which the Signature is added
    /// </summary>
    public int ProcessStepID { get; set; }

    /// <summary>
    /// List of attachment ids in a3 format
    /// </summary>
    public List<string> SignedAttachmentDataIds { get; set; }

    /// <summary>
    /// List of form ids in a3 format
    /// </summary>
    public List<string> SignedFormDataIds { get; set; }
}

/// <summary>
/// Entity for holding information about a payment, reflects PaymentInfo DB table.
/// </summary>
public class PaymentView
{
    /// <summary>
    /// Gets or sets PaymentID
    /// </summary>
    public int PaymentID { get; set; }

    /// <summary>
    /// Gets or sets reference to Payment Metadata ID
    /// </summary>
    public int PaymentMetadataID_FK { get; set; }

    /// <summary>
    /// Gets or sets Payment Sum
    /// </summary>
    public int PaymentSum { get; set; }

    /// <summary>
    /// Gets or sets Description for the payment
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// Gets or sets the TransactionID
    /// </summary>
    public string TransactionId { get; set; }

    /// <summary>
    /// Gets or sets the OrderID
    /// </summary>
    public string OrderId { get; set; }

    /// <summary>
    /// Gets or sets reference to ReporteeElementID
    /// </summary>
    public int ReporteeElementId { get; set; }

    /// <summary>
    /// Gets or sets Created Date
    /// </summary>
    public DateTime CreatedDate { get; set; }

    /// <summary>
    /// Gets or sets Last Update Date
    /// </summary>
    public DateTime LastUpdatedDate { get; set; }

    /// <summary>
    /// Gets or sets the Status for the payment
    /// </summary>
    public int Status { get; set; }

    /// <summary>
    /// Gets or sets ClientIP
    /// </summary>
    public string ClientIP { get; set; }

    /// <summary>
    /// Gets or sets PartyID
    /// </summary>
    public int PartyId { get; set; }

    /// <summary>
    /// Gets or sets Reference
    /// </summary>
    public string Reference { get; set; }
}
