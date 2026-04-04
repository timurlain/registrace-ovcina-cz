# These Gherkin scenarios are the acceptance-spec source of truth for
# future Playwright E2E coverage. Keep scenario names close to the final
# executable test names.

@e2e @v1
Feature: Household registration for the Ovčina game
  As a registrant or organizer
  I want the registration app to support household submissions and core operations
  So that one game can be registered and managed end to end

  Background:
    Given a published game "Ovčina 2026" exists
    And the game registration cutoff is in the future
    And the game meal-ordering cutoff is in the future
    And the game has player base price 1200 CZK
    And the game has adult helper base price 0 CZK
    And the game has at least one kingdom target configured
    And an admin account exists
    And a registrant account exists
    And another registrant account exists

  Rule: Authentication and entry

    @critical
    Scenario: Admin logs in through the login form and opens game management
      Given the admin is signed out
      When the admin opens the login page
      And submits valid admin credentials
      And opens the game management page
      Then the game creation form is shown
      And no authorization error is shown

    @critical
    Scenario: Unauthenticated visitor is redirected to the login page
      Given an unauthenticated visitor
      When the visitor opens a protected submissions page
      Then the visitor is redirected to the login page
      And the login form is shown

  Rule: Registrant draft lifecycle

    @critical
    Scenario: Registrant creates a first household draft for a game
      Given the registrant is signed in
      When the registrant starts registration for the published game
      Then a draft submission is created for that game
      And the draft belongs to the registrant
      And the draft shows household contact fields
      And the draft initially contains no attendees

    @critical
    Scenario: Registrant resumes the same draft for the same game
      Given the registrant already has a draft submission for the published game
      When the registrant starts registration for the same game again
      Then the existing draft is opened
      And no duplicate submission is created

    @important
    Scenario: Registrant dashboard shows only overall remaining player spots
      Given the registrant is signed in
      When the registrant opens the registration dashboard
      Then the game card shows overall remaining player spots
      And no per-kingdom slot guarantee is shown

  Rule: Household attendee management

    @critical
    Scenario: Registrant adds a child player and an adult helper to one submission
      Given the registrant has a draft submission for the published game
      When the registrant adds a child attendee with role "Player"
      And the registrant adds an adult attendee with role "Npc"
      Then the submission contains 2 attendees
      And both attendees belong to the same submission
      And the total price reflects only the player

    @important
    Scenario: Registrant removes an attendee from a draft submission
      Given the registrant has a draft submission with two attendees
      When the registrant removes one attendee
      Then only one attendee remains in the submission
      And the total price is recalculated

    @important
    Scenario: Player can choose a preferred kingdom as advisory input
      Given the registrant has a draft submission for the published game
      When the registrant adds a player attendee with preferred kingdom "Elfové"
      Then the attendee shows preferred kingdom "Elfové"
      And the UI does not present the preference as a guaranteed placement

  Rule: Validation rules

    @critical
    Scenario: Minor attendee cannot be added without guardian authorization
      Given the registrant has a draft submission for the published game
      When the registrant tries to add a minor attendee without guardian name, relationship, or consent
      Then the attendee is not added
      And a guardian validation error is shown

    @important
    Scenario: Adult helper can be added without guardian data
      Given the registrant has a draft submission for the published game
      When the registrant adds an adult attendee with role "Npc"
      Then the attendee is added
      And no guardian validation error is shown

    @critical
    Scenario: Submission cannot be sent without household contact information
      Given the registrant has a draft submission with at least one attendee
      And the household contact information is incomplete
      When the registrant submits the submission
      Then the submission remains a draft
      And household contact validation is shown

    @critical
    Scenario: Submission cannot be sent without attendees
      Given the registrant has a draft submission with complete household contact information
      And the submission has no attendees
      When the registrant submits the submission
      Then the submission remains a draft
      And attendee requirement validation is shown

  Rule: Submission and payment

    @critical
    Scenario: Submitted household shows QR and final total
      Given the registrant has a valid draft with one child player and one adult helper
      And the household contact information is complete
      When the registrant submits the submission
      Then the submission is marked as submitted
      And a SPAYD payment QR is shown
      And the displayed total is "1200,00 Kč"
      And the submission shows an unpaid balance state

  Rule: Authorization and ownership isolation

    @critical
    Scenario: One registrant cannot open another registrant's submission
      Given the first registrant has a draft submission
      And the second registrant is signed in
      When the second registrant opens the first registrant's submission URL
      Then access is denied or the submission is not found
      And the second registrant cannot see the household data

  Rule: Agreed behaviors not fully implemented yet

    @planned-gap
    Scenario: Submitted household remains editable until cutoff
      Given the registrant has submitted a household before the registration cutoff
      When the registrant changes the submission before the cutoff
      Then the submission is updated
      And the expected total is recalculated
      And the balance state reflects the new expected amount

    @planned-gap
    Scenario: Returning attendee is suggested from history instead of always creating a new person
      Given historical people and registrations exist for the same household member
      When the registrant starts a new submission
      And enters matching attendee details
      Then the app suggests the historical person match
      And the organizer can confirm the identity if needed
